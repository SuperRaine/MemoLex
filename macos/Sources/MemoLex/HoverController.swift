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
    private var outsideSince: Date?
    private var verifying = false
    init(model: AppModel) { self.model = model }
    func configure() { stop(); if model.preferences.hoverEnabled { timer = Timer.scheduledTimer(withTimeInterval: 0.10, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } } } }
    func stop() { timer?.invalidate(); timer = nil; generation += 1; task?.cancel(); panel?.orderOut(nil); panel = nil; attempted = false; fingerprint = nil; outsideSince = nil }
    private func modifiersAllowed() -> Bool {
        let required = model.preferences.hoverModifiers; let flags = NSEvent.modifierFlags
        return (required & 1 == 0 || flags.contains(.control)) && (required & 2 == 0 || flags.contains(.option)) && (required & 4 == 0 || flags.contains(.shift))
    }
    private func tick() {
        let point = NSEvent.mouseLocation; let quartz = ScreenText.quartz(point)
        if let panel, panel.isVisible {
            if panel.frame.insetBy(dx: -7, dy: -7).contains(point)
                || wordBounds.insetBy(dx: -3, dy: -3).contains(quartz) {
                outsideSince = nil
                verifyImage()
                return
            }
            let now = Date()
            let left = outsideSince ?? now
            outsideSince = left
            if now.timeIntervalSince(left) < 0.23 { return }
            panel.orderOut(nil)
            outsideSince = nil
        }
        if point != lastPoint {
            lastPoint = point; stoppedAt = Date(); attempted = false; generation += 1; task?.cancel(); panel?.orderOut(nil); fingerprint = nil
        }
        guard model.preferences.hoverEnabled, !attempted, modifiersAllowed(), Date().timeIntervalSince(stoppedAt) * 1000 >= Double(model.preferences.hoverDelay), model.sheet == nil, model.tourIndex == nil, NSWorkspace.shared.frontmostApplication?.processIdentifier != ProcessInfo.processInfo.processIdentifier else { return }
        attempted = true; let version = generation
        task = Task {
            var candidate = PermissionCenter.accessibility ? ScreenText.accessible(at: quartz) : nil
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
                if value["entry"] != .null, model.preferences.hoverAutoAdd, !model.writing,
                   let target = model.hoverBookmarkTarget {
                    let word = value["word"].text
                    Task { try? await model.saveHoverBookmark(word, target: target) }
                }
            } catch { }
        }
    }
    private func verifyImage() {
        guard let original = fingerprint, !verifying, Date().timeIntervalSince(lastVerification) > 0.8 else { return }
        guard PermissionCenter.screen else { panel?.orderOut(nil); fingerprint = nil; return }
        verifying = true; lastVerification = Date(); let version = generation; let rect = captureRect
        Task {
            defer { verifying = false }
            if let image = try? await ScreenCapture.image(rect), version == generation, !ScreenText.stable(original, ScreenText.fingerprint(image)) { panel?.orderOut(nil); fingerprint = nil }
        }
    }
    private func show(_ value: J, bounds: CGRect, cursor: CGPoint) {
        let height: CGFloat = 238
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 360, height: height), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        let size: CGFloat = model.preferences.hoverFontSize == "large" ? 17 : model.preferences.hoverFontSize == "small" ? 12 : 14
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = true; panel.level = .floating; panel.hidesOnDeactivate = false; panel.isReleasedWhenClosed = false; panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = NSHostingView(rootView: HoverView(model: model, value: value, fontSize: size, height: height) { [weak self] word in self?.panel?.orderOut(nil); self?.model.lookup(word) })
        panel.alphaValue = model.preferences.hoverTranslucent ? 0.89 : 1
        let available = NSScreen.screens.first { $0.frame.contains(cursor) }?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1000, height: 750)
        let x = min(max(cursor.x + 8, available.minX + 6), available.maxX - 366); var y = cursor.y - height - 8
        if y < available.minY { y = cursor.y + 8 }
        panel.setFrameOrigin(CGPoint(x: x, y: min(max(y, available.minY + 6), available.maxY - height - 6)))
        self.panel?.orderOut(nil); self.panel = panel; wordBounds = bounds; panel.orderFrontRegardless()
    }
}

private struct HoverView: View {
    @ObservedObject var model: AppModel
    let value: J
    let fontSize: CGFloat
    let height: CGFloat
    let navigate: (String) -> Void
    @State private var error = ""
    var body: some View {
        let phonetic = value["entry"]["phonetic"].text
        VStack(alignment: .leading, spacing: 10) {
            Button { navigate(value["word"].text) } label: {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(value["word"].text)
                        .font(.system(size: fontSize + 6, weight: .bold))
                        .foregroundStyle(Palette.ink)
                        .lineLimit(1)
                    if !phonetic.isEmpty {
                        Text("/\(phonetic)/")
                            .font(.system(size: max(11, fontSize - 1)))
                            .foregroundStyle(Palette.muted)
                            .lineLimit(1)
                    }
                    Spacer(minLength: 4)
                    Image(systemName: "arrow.up.right")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                }
            }.buttonStyle(.plain)
            if value["entry"] != .null { Text(value["entry"]["translation"].text.decodedLines).font(.system(size: fontSize)).lineLimit(3).lineSpacing(3).foregroundStyle(Palette.ink) }
            else { Text("未找到释义，可试试：").font(.system(size: fontSize)).foregroundStyle(Palette.muted); HStack { ForEach(Array(value["suggestions"].list.prefix(3)), id: \.text) { word in Button(word.text) { navigate(word.text) }.buttonStyle(.plain).foregroundStyle(Palette.deep) } } }
            if let note = value["notes"].list.first {
                HStack(alignment: .top, spacing: 6) {
                    Text("记")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 5).padding(.vertical, 2)
                        .background(Palette.accent.opacity(0.2), in: RoundedRectangle(cornerRadius: 4))
                    Text(note["text"].text)
                        .font(.system(size: max(11, fontSize - 1)))
                        .lineLimit(2)
                        .foregroundStyle(Palette.deep)
                }
                .padding(.horizontal, 9).padding(.vertical, 6)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.wash, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Palette.accent.opacity(0.22), lineWidth: 0.7))
            }
            Spacer(minLength: 0)
            if value["entry"] != .null {
                Divider().overlay(Palette.border.opacity(0.8))
                bookmark
            }
        }.padding(15).frame(width: 360, height: height, alignment: .topLeading).background(Palette.paper, in: RoundedRectangle(cornerRadius: 14, style: .continuous)).overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).stroke(Palette.accent.opacity(0.45), lineWidth: 1))
    }

    private var bookmark: some View {
        let target = model.hoverBookmarkTarget
        let word = value["word"].text
        let state = model.hoverBookmarkStatus(word, target: target)
        let hint = target.map { "加入「\($0.title)」" } ?? (model.loggedIn ? "请先选择一个云词本" : "登录墨墨账号后可收藏")
        return HStack(spacing: 8) {
            if !error.isEmpty || target == nil {
                Text(!error.isEmpty ? error : hint).font(.caption).foregroundStyle(Palette.muted)
                    .lineLimit(1).help(error)
            } else {
                Button("查看完整释义 →") { navigate(word) }
                    .buttonStyle(.plain)
                    .font(.system(size: 11.5, weight: .medium))
                    .foregroundStyle(Palette.deep)
            }
            Spacer(minLength: 5)
            if state.saved {
                Text("✓ 已收藏")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Color.white)
                    .padding(.horizontal, 11).frame(height: 28)
                    .background(Color(red: 0.09, green: 0.68, blue: 0.46), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                    .help(hint)
            } else {
                Button(state.pending ? "收藏中…" : "＋ 收藏") {
                    guard let target else { return }
                    error = ""
                    Task {
                        do { try await model.saveHoverBookmark(word, target: target) }
                        catch { if model.hoverBookmarkTarget == target { self.error = error.localizedDescription } }
                    }
                }.buttonStyle(MemoButtonStyle(accent: true, compact: true))
                    .disabled(target == nil || state.pending)
                    .help(hint)
            }
        }.onChange(of: target) { _ in error = "" }
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
        if PermissionCenter.screen { beginSelection(); return }
        model.showWindow?()
        model.notice = .init(title: "需要屏幕录制权限",
                             message: "在系统设置中允许墨墨搜词后即可截图。列表里找不到时，可用「＋」添加当前 App。",
                             confirm: "前往系统设置", action: { PermissionCenter.openSettings(for: .screen) })
    }
    private func beginSelection() {
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
