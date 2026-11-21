import AppKit
import SwiftUI

@MainActor final class HoverController {
    private unowned let model: AppModel
    private var timer: Timer?
    private var task: Task<Void, Never>?
    private var lastPoint = CGPoint(x: -99999, y: -99999)
    private var stoppedAt = Date()
    private var attempted = false
    private var generation = 0
    private var panel: NSPanel?
    private var wordBounds = CGRect.zero
    private var fingerprint: [UInt8]?
    private var captureRect = CGRect.zero
    private var lastVerification = Date.distantPast
    private var verifying = false
    init(model: AppModel) { self.model = model }
    func configure() { stop(); if model.preferences.hoverEnabled { timer = Timer.scheduledTimer(withTimeInterval: 0.10, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } } } }
    func stop() { timer?.invalidate(); timer = nil; generation += 1; task?.cancel(); panel?.orderOut(nil); panel = nil; attempted = false; fingerprint = nil }
    private func modifiersAllowed() -> Bool {
        let required = model.preferences.hoverModifiers; let flags = NSEvent.modifierFlags
        return (required & 1 == 0 || flags.contains(.control)) && (required & 2 == 0 || flags.contains(.option)) && (required & 4 == 0 || flags.contains(.shift))
    }
    private func tick() {
        let point = NSEvent.mouseLocation; let quartz = ScreenText.quartz(point)
        if let panel, panel.isVisible, (panel.frame.insetBy(dx: -7, dy: -7).contains(point) || wordBounds.insetBy(dx: -3, dy: -3).contains(quartz)), modifiersAllowed() {
            verifyImage(); return
        }
        if point != lastPoint {
            lastPoint = point; stoppedAt = Date(); attempted = false; generation += 1; task?.cancel(); panel?.orderOut(nil); fingerprint = nil
        }
        guard model.preferences.hoverEnabled, !attempted, modifiersAllowed(), Date().timeIntervalSince(stoppedAt) * 1000 >= Double(model.preferences.hoverDelay), model.sheet == nil, model.tourIndex == nil, NSWorkspace.shared.frontmostApplication?.processIdentifier != ProcessInfo.processInfo.processIdentifier else { return }
        attempted = true; let version = generation
        task = Task {
            var candidate = ScreenText.accessible(at: quartz)
            var capturedFingerprint: [UInt8]?
            var crop = CGRect.zero
            if candidate == nil, PermissionCenter.screen {
                let screen = NSScreen.screens.first(where: { $0.frame.contains(point) })?.frame ?? .zero
                let bounds = CGRect(x: screen.minX, y: ScreenText.primaryHeight - screen.maxY, width: screen.width, height: screen.height)
                crop = bounds.intersection(CGRect(x: quartz.x - 220, y: quartz.y - 45, width: 440, height: 90))
                do {
                    let first = try await ScreenCapture.image(crop); let initial = ScreenText.fingerprint(first)
                    try await Task.sleep(nanoseconds: 180_000_000)
                    let second = try await ScreenCapture.image(crop)
                    guard ScreenText.stable(initial, ScreenText.fingerprint(second)), version == generation else { return }
                    let words = try await ScreenText.recognize(second, crop: crop, pointer: quartz).1
                    let selection = try await model.core.request("hover.pick", ["point": .object(["x": .number(quartz.x), "y": .number(quartz.y)]), "candidates": .array(words.map { word in .object(["word": .string(word.word), "confidence": .number(Double(word.confidence) * 100), "method": .string("Vision"), "bounds": .object(["x": .number(word.bounds.minX), "y": .number(word.bounds.minY), "width": .number(word.bounds.width), "height": .number(word.bounds.height)])]) })])
                    if selection != .null { let box = selection["bounds"]; candidate = ScreenWord(word: selection["word"].text, bounds: CGRect(x: box["x"].doubleValue, y: box["y"].doubleValue, width: box["width"].doubleValue, height: box["height"].doubleValue)) }; capturedFingerprint = initial
                } catch { return }
            }
            guard let candidate, version == generation, !Task.isCancelled, ScreenText.englishWord(candidate.word) else { return }
            do {
                let value = try await model.core.request("dictionary.lookup", ["word": .string(candidate.word)])
                guard version == generation, !Task.isCancelled else { return }
                if value["entry"] == .null, !model.preferences.hoverNearest { return }
                self.fingerprint = capturedFingerprint; self.captureRect = crop
                self.show(value, bounds: candidate.bounds, cursor: point)
                if value["entry"] != .null, model.preferences.hoverAutoAdd, model.loggedIn, model.selectedPad != .null {
                    let saved = try? await model.core.request("notepads.add", ["id": model.selectedPad["id"], "word": value["word"]])
                    if let pad = saved?["notepad"], pad["id"] == model.selectedPad["id"] { model.selectedPad = pad; await model.refreshMembership() }
                }
            } catch { }
        }
    }
    private func verifyImage() {
        guard let original = fingerprint, !verifying, Date().timeIntervalSince(lastVerification) > 0.8 else { return }
        verifying = true; lastVerification = Date(); let version = generation; let rect = captureRect
        Task {
            defer { verifying = false }
            if let image = try? await ScreenCapture.image(rect), version == generation, !ScreenText.stable(original, ScreenText.fingerprint(image)) { panel?.orderOut(nil); fingerprint = nil }
        }
    }
    private func show(_ value: J, bounds: CGRect, cursor: CGPoint) {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 350, height: 180), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        let size: CGFloat = model.preferences.hoverFontSize == "large" ? 17 : model.preferences.hoverFontSize == "small" ? 12 : 14
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = true; panel.level = .floating; panel.hidesOnDeactivate = false; panel.isReleasedWhenClosed = false; panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = NSHostingView(rootView: HoverView(value: value, fontSize: size) { [weak self] word in self?.panel?.orderOut(nil); self?.model.lookup(word) })
        panel.alphaValue = model.preferences.hoverTranslucent ? 0.89 : 1
        let available = NSScreen.screens.first { $0.frame.contains(cursor) }?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1000, height: 750)
        let x = min(max(cursor.x + 12, available.minX + 6), available.maxX - 356); var y = cursor.y - 192
        if y < available.minY { y = cursor.y + 20 }
        panel.setFrameOrigin(CGPoint(x: x, y: min(max(y, available.minY + 6), available.maxY - 186)))
        self.panel?.orderOut(nil); self.panel = panel; wordBounds = bounds; panel.orderFrontRegardless()
    }
}

private struct HoverView: View {
    let value: J
    let fontSize: CGFloat
    let navigate: (String) -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Button { navigate(value["word"].text) } label: { HStack { Text(value["word"].text).font(.system(size: fontSize + 6, weight: .semibold)); Spacer(); Image(systemName: "arrow.up.right") } }.buttonStyle(.plain).foregroundStyle(Palette.deep)
            if value["entry"] != .null { Text(value["entry"]["translation"].text.decodedLines).font(.system(size: fontSize)).lineLimit(3).foregroundStyle(Palette.ink) }
            else { Text("未找到释义，可试试：").font(.system(size: fontSize)).foregroundStyle(Palette.muted); HStack { ForEach(Array(value["suggestions"].list.prefix(3)), id: \.text) { word in Button(word.text) { navigate(word.text) }.buttonStyle(.plain).foregroundStyle(Palette.deep) } } }
            if let note = value["notes"].list.first { Text(note["text"].text).font(.system(size: max(11, fontSize - 1))).lineLimit(3).foregroundStyle(Palette.deep) }
        }.padding(16).frame(width: 350, height: 180, alignment: .topLeading).background(Palette.paper, in: RoundedRectangle(cornerRadius: 12)).overlay(RoundedRectangle(cornerRadius: 12).stroke(Palette.border))
    }
}

@MainActor final class ScreenshotCapture {
    private unowned let model: AppModel
    private var panels: [NSPanel] = []
    private var monitor: Any?
    private var wasVisible = false
    init(model: AppModel) { self.model = model }
    func start() {
        guard panels.isEmpty else { return }
        guard PermissionCenter.screen else { PermissionCenter.requestScreen(); model.notice = .init(title: "允许屏幕录制", message: "请在系统设置中允许墨墨搜词录制屏幕，然后重试截图翻译。"); return }
        wasVisible = NSApp.windows.contains { $0.isVisible && !($0 is NSPanel) }; NSApp.windows.filter { !($0 is NSPanel) }.forEach { $0.orderOut(nil) }
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in if event.keyCode == 53 { self?.cancel(); return nil }; return event }
        for screen in NSScreen.screens {
            let panel = SelectionPanel(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
            panel.isOpaque = false; panel.backgroundColor = .clear; panel.level = .screenSaver; panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]; panel.isReleasedWhenClosed = false
            let view = SelectionView(frame: NSRect(origin: .zero, size: screen.frame.size)); view.selected = { [weak self] rect in
                let global = rect.offsetBy(dx: screen.frame.minX, dy: screen.frame.minY)
                let quartz = CGRect(x: global.minX, y: ScreenText.primaryHeight - global.maxY, width: global.width, height: global.height)
                self?.capture(quartz)
            }; view.cancelled = { [weak self] in self?.cancel() }; panel.contentView = view; panels.append(panel); panel.makeKeyAndOrderFront(nil)
        }; NSApp.activate(ignoringOtherApps: true)
    }
    func cancel() { closePanels(); if wasVisible { model.showWindow?() } }
    private func closePanels() { panels.forEach { $0.orderOut(nil) }; panels = []; if let monitor { NSEvent.removeMonitor(monitor) }; monitor = nil }
    private func capture(_ rect: CGRect) {
        closePanels()
        guard rect.width >= 4, rect.height >= 4 else { if wasVisible { model.showWindow?() }; return }
        Task {
            do { try await Task.sleep(nanoseconds: 120_000_000); let image = try await ScreenCapture.image(rect); let result = try await ScreenText.recognize(image, crop: rect); model.showWindow?(); await model.classify(result.0) }
            catch { model.showWindow?(); model.fail(CoreFailure(code: "capture", message: "未能读取截图，请检查屏幕录制权限后重试。")) }
        }
    }
}
private final class SelectionPanel: NSPanel { override var canBecomeKey: Bool { true } }
private final class SelectionView: NSView {
    var selected: ((CGRect) -> Void)?
    var cancelled: (() -> Void)?
    private var start: CGPoint?
    private var current: CGPoint?
    override var acceptsFirstResponder: Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }
    override func mouseDown(with event: NSEvent) { start = convert(event.locationInWindow, from: nil); current = start; needsDisplay = true }
    override func mouseDragged(with event: NSEvent) { current = convert(event.locationInWindow, from: nil); needsDisplay = true }
    override func mouseUp(with event: NSEvent) { guard let start else { return }; let end = convert(event.locationInWindow, from: nil); selected?(CGRect(x: min(start.x, end.x), y: min(start.y, end.y), width: abs(end.x - start.x), height: abs(end.y - start.y))) }
    override func rightMouseDown(with event: NSEvent) { cancelled?() }
    override func keyDown(with event: NSEvent) { if event.keyCode == 53 { cancelled?() } }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.black.withAlphaComponent(0.25).setFill(); bounds.fill()
        if let start, let current { let rect = CGRect(x: min(start.x, current.x), y: min(start.y, current.y), width: abs(current.x - start.x), height: abs(current.y - start.y)); NSColor.clear.setFill(); rect.fill(using: .copy); NSColor.systemBlue.setStroke(); let border = NSBezierPath(rect: rect); border.lineWidth = 2; border.stroke() }
        let title = "拖动选择翻译范围 · Esc 取消" as NSString
        title.draw(at: CGPoint(x: 24, y: bounds.height - 50), withAttributes: [.font: NSFont.systemFont(ofSize: 17, weight: .semibold), .foregroundColor: NSColor.white])
    }
}
