import AppKit
import ApplicationServices
import CoreMedia
import ScreenCaptureKit
import Vision

struct ScreenWord {
    var word: String
    var bounds: CGRect // Quartz screen coordinates, in points.
}

enum ScreenText {
    static let pattern = "[A-Za-z]+(?:['’-][A-Za-z]+)*"
    static func englishWord(_ text: String) -> Bool { text.range(of: "^" + pattern + "$", options: .regularExpression) != nil && text.count <= 60 }
    static var primaryHeight: CGFloat { CGDisplayBounds(CGMainDisplayID()).height }
    static func quartz(_ point: CGPoint) -> CGPoint { CGPoint(x: point.x, y: primaryHeight - point.y) }
    static func appKit(_ rect: CGRect) -> CGRect { CGRect(x: rect.minX, y: primaryHeight - rect.maxY, width: rect.width, height: rect.height) }

    static func accessible(at point: CGPoint) -> ScreenWord? {
        guard AXIsProcessTrusted() else { return nil }
        var element: AXUIElement?
        let system = AXUIElementCreateSystemWide(); AXUIElementSetMessagingTimeout(system, 0.2)
        guard AXUIElementCopyElementAtPosition(system, Float(point.x), Float(point.y), &element) == .success, let element else { return nil }
        AXUIElementSetMessagingTimeout(element, 0.2)
        return read(element, point: point, depth: 0)
    }
    private static func read(_ element: AXUIElement, point: CGPoint, depth: Int) -> ScreenWord? {
        guard depth < 5 else { return nil }
        var location = point
        if let parameter = AXValueCreate(.cgPoint, &location) {
            var rangeValue: CFTypeRef?
            if AXUIElementCopyParameterizedAttributeValue(element, kAXRangeForPositionParameterizedAttribute as CFString, parameter, &rangeValue) == .success, let rangeValue, CFGetTypeID(rangeValue) == AXValueGetTypeID() {
                var position = CFRange()
                AXValueGetValue(unsafeBitCast(rangeValue, to: AXValue.self), .cfRange, &position)
                if position.location >= 0 {
                    var fullValue: CFTypeRef?
                    AXUIElementCopyAttributeValue(element, kAXValueAttribute as CFString, &fullValue)
                    if let text = fullValue as? String, (text as NSString).length < 200_000, let result = word(in: text, at: position.location, element: element, point: point) { return result }
                }
            }
        }
        for attribute in [kAXValueAttribute, kAXTitleAttribute, kAXDescriptionAttribute] {
            var value: CFTypeRef?
            if AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success, let text = value as? String, englishWord(text.trimmed), let rect = bounds(element), rect.contains(point), rect.width < 600, rect.height < 140 { return .init(word: text.trimmed, bounds: rect) }
        }
        var children: CFTypeRef?
        if AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &children) == .success, let children = children as? [AXUIElement] {
            for child in children.prefix(40) { if let rect = bounds(child), rect.contains(point), let result = read(child, point: point, depth: depth + 1) { return result } }
        }
        return nil
    }
    private static func word(in text: String, at index: Int, element: AXUIElement, point: CGPoint) -> ScreenWord? {
        let source = text as NSString
        let regex = try? NSRegularExpression(pattern: pattern)
        for match in regex?.matches(in: text, range: NSRange(location: 0, length: source.length)) ?? [] where index >= match.range.location && index < NSMaxRange(match.range) {
            var range = CFRange(location: match.range.location, length: match.range.length)
            guard let parameter = AXValueCreate(.cfRange, &range) else { return nil }
            var result: CFTypeRef?
            guard AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute as CFString, parameter, &result) == .success, let result, CFGetTypeID(result) == AXValueGetTypeID() else { return nil }
            var rect = CGRect.zero
            AXValueGetValue(unsafeBitCast(result, to: AXValue.self), .cgRect, &rect)
            guard rect.insetBy(dx: -1, dy: -1).contains(point) else { return nil }
            return .init(word: source.substring(with: match.range), bounds: rect)
        }
        return nil
    }
    private static func bounds(_ element: AXUIElement) -> CGRect? {
        var a: CFTypeRef?; var b: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXPositionAttribute as CFString, &a) == .success, AXUIElementCopyAttributeValue(element, kAXSizeAttribute as CFString, &b) == .success, let a, let b, CFGetTypeID(a) == AXValueGetTypeID(), CFGetTypeID(b) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero; var size = CGSize.zero
        AXValueGetValue(unsafeBitCast(a, to: AXValue.self), .cgPoint, &point); AXValueGetValue(unsafeBitCast(b, to: AXValue.self), .cgSize, &size)
        return CGRect(origin: point, size: size)
    }
    static func recognize(_ image: CGImage, crop: CGRect, pointer: CGPoint? = nil) async throws -> (String, ScreenWord?) {
        try await Task.detached(priority: .userInitiated) {
            let request = VNRecognizeTextRequest(); request.recognitionLevel = .accurate; request.recognitionLanguages = ["en-US"]; request.usesLanguageCorrection = false
            if #available(macOS 13.0, *) { request.automaticallyDetectsLanguage = false }
            try VNImageRequestHandler(cgImage: image).perform([request])
            let observations = request.results ?? []
            let text = observations.compactMap { observation -> String? in guard let candidate = observation.topCandidates(1).first, candidate.confidence >= 0.55 else { return nil }; return candidate.string }.joined(separator: "\n")
            guard let pointer else { return (text, nil) }
            for observation in observations {
                guard let candidate = observation.topCandidates(1).first, candidate.confidence >= 0.86 else { continue }
                let string = candidate.string; let regex = try NSRegularExpression(pattern: pattern)
                for match in regex.matches(in: string, range: NSRange(location: 0, length: (string as NSString).length)) {
                    guard match.range.length >= 3, let range = Range(match.range, in: string), let box = try? candidate.boundingBox(for: range) else { continue }
                    let normalized = box.boundingBox
                    let rect = CGRect(x: crop.minX + normalized.minX * crop.width, y: crop.minY + (1 - normalized.maxY) * crop.height, width: normalized.width * crop.width, height: normalized.height * crop.height)
                    if rect.insetBy(dx: -1, dy: -1).contains(pointer) { return (text, ScreenWord(word: String(string[range]), bounds: rect)) }
                }
            }
            return (text, nil)
        }.value
    }
    static func fingerprint(_ image: CGImage) -> [UInt8] {
        var bytes = [UInt8](repeating: 0, count: 128 * 32)
        bytes.withUnsafeMutableBytes { memory in
            guard let context = CGContext(data: memory.baseAddress, width: 128, height: 32, bitsPerComponent: 8, bytesPerRow: 128, space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return }
            context.draw(image, in: CGRect(x: 0, y: 0, width: 128, height: 32))
        }; return bytes
    }
    static func stable(_ first: [UInt8], _ second: [UInt8]) -> Bool {
        guard first.count == second.count, !first.isEmpty else { return false }
        return zip(first, second).reduce(0) { $0 + abs(Int($1.0) - Int($1.1)) } < first.count * 2
    }
}

enum ScreenCapture {
    static func image(_ rect: CGRect) async throws -> CGImage {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let display = content.displays.first(where: { CGDisplayBounds($0.displayID).contains(CGPoint(x: rect.midX, y: rect.midY)) }) else { throw CoreFailure(code: "capture", message: "未找到要截取的屏幕。") }
        let screen = CGDisplayBounds(display.displayID); let clipped = screen.intersection(rect)
        guard !clipped.isEmpty else { throw CoreFailure(code: "capture", message: "截图范围为空。") }
        let excluded = content.windows.filter { $0.owningApplication?.processID == ProcessInfo.processInfo.processIdentifier }
        let filter = SCContentFilter(display: display, excludingWindows: excluded)
        let config = SCStreamConfiguration(); config.sourceRect = clipped.offsetBy(dx: -screen.minX, dy: -screen.minY); config.width = Int(clipped.width * 2); config.height = Int(clipped.height * 2); config.showsCursor = false; config.capturesAudio = false
        if #available(macOS 14.0, *) { return try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config) }
        return try await LegacyScreenFrame.capture(filter: filter, configuration: config)
    }
}

private final class LegacyScreenFrame: NSObject, SCStreamOutput {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<CGImage, Error>?
    private var stream: SCStream?
    static func capture(filter: SCContentFilter, configuration: SCStreamConfiguration) async throws -> CGImage {
        let capture = LegacyScreenFrame()
        return try await withCheckedThrowingContinuation { continuation in
            capture.continuation = continuation
            do {
                let stream = SCStream(filter: filter, configuration: configuration, delegate: nil); capture.stream = stream
                try stream.addStreamOutput(capture, type: .screen, sampleHandlerQueue: DispatchQueue(label: "MemoLex.ScreenFrame"))
                stream.startCapture { error in if let error { capture.finish(.failure(error)) } }
                DispatchQueue.global().asyncAfter(deadline: .now() + 4) { capture.finish(.failure(CoreFailure(code: "capture", message: "屏幕截图超时。"))) }
            } catch { capture.finish(.failure(error)) }
        }
    }
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, sampleBuffer.isValid, let buffer = sampleBuffer.imageBuffer else { return }
        let ci = CIImage(cvPixelBuffer: buffer)
        if let image = CIContext().createCGImage(ci, from: ci.extent) { finish(.success(image)) }
    }
    private func finish(_ result: Result<CGImage, Error>) {
        lock.lock(); let pending = continuation; continuation = nil; let stream = stream; self.stream = nil; lock.unlock()
        guard let pending else { return }; stream?.stopCapture { _ in }; pending.resume(with: result)
    }
}
