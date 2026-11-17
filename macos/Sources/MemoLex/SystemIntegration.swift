import AppKit
import ApplicationServices
import Carbon
import Foundation
import ServiceManagement
import SwiftUI

@MainActor enum PermissionCenter {
    static var accessibility: Bool { AXIsProcessTrusted() }
    static var screen: Bool { CGPreflightScreenCaptureAccess() }
    static func requestAccessibility() { _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary) }
    static func requestScreen() { _ = CGRequestScreenCaptureAccess() }
    static func open(_ section: String) { if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(section)") { NSWorkspace.shared.open(url) } }
}

struct PermissionsSettings: View {
    @State private var refresh = 0
    var body: some View {
        VStack(alignment: .leading, spacing: 17) {
            permission("辅助功能", enabled: PermissionCenter.accessibility, detail: "用于读取其他软件中的英文文本、监听连续两次 ⌘C。") { PermissionCenter.requestAccessibility(); PermissionCenter.open("Privacy_Accessibility") }
            permission("屏幕录制", enabled: PermissionCenter.screen, detail: "用于图片取词和截图翻译。识别在本机完成。") { PermissionCenter.requestScreen(); PermissionCenter.open("Privacy_ScreenCapture") }
            Button("刷新权限状态") { refresh += 1 }.buttonStyle(MemoButtonStyle())
        }.id(refresh).tourTarget("permissions")
    }
    private func permission(_ title: String, enabled: Bool, detail: String, action: @escaping () -> Void) -> some View { SettingsCard(title: title) { HStack { Text(enabled ? "已允许" : "尚未允许").foregroundStyle(enabled ? Palette.deep : Palette.muted); Spacer(); Button("前往系统设置", action: action).buttonStyle(MemoButtonStyle(compact: true)) }; Text(detail).font(.system(size: 12)).foregroundStyle(Palette.muted) } }
}

@MainActor enum LoginService {
    static var enabled: Bool { SMAppService.mainApp.status == .enabled }
    static func apply(_ enabled: Bool) throws {
        if enabled && SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() }
        else if !enabled && SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
    }
}

struct ShortcutSettings: View {
    @ObservedObject var preferences: Preferences
    private let rows = [("search", "唤起快速查词"), ("clipboard", "查询剪贴板"), ("screenshot", "截图翻译")]
    var body: some View {
        VStack(alignment: .leading, spacing: 15) {
            ForEach(rows, id: \.0) { key, title in HStack { Text(title).frame(width: 160, alignment: .leading); ShortcutRecorder(shortcut: Binding(get: { preferences.shortcuts[key] ?? NativeShortcut.defaults[key]! }, set: { preferences.shortcuts[key] = $0 })).frame(width: 190, height: 36) } }
            Text("全局快捷键可在其他软件中使用。点击输入框后按下组合键。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            Divider()
            Text("⌘L 快速查词   ·   ⌘Y 查词历史   ·   ⌘[ / ⌘] 后退或前进").foregroundStyle(Palette.muted)
            Text("⌘T 段落翻译   ·   ⌘↩ 加入云词本或开始翻译").foregroundStyle(Palette.muted)
        }
    }
}

private struct ShortcutRecorder: NSViewRepresentable {
    @Binding var shortcut: NativeShortcut
    func makeNSView(context: Context) -> KeyField { let field = KeyField(); field.onRecord = { shortcut = $0 }; field.stringValue = shortcut.label; field.isEditable = false; field.isBezeled = true; field.bezelStyle = .roundedBezel; field.alignment = .center; return field }
    func updateNSView(_ field: KeyField, context: Context) { field.stringValue = shortcut.label; field.onRecord = { shortcut = $0 } }
    final class KeyField: NSTextField {
        var onRecord: ((NativeShortcut) -> Void)?
        override var acceptsFirstResponder: Bool { true }
        override func mouseDown(with event: NSEvent) { window?.makeFirstResponder(self); stringValue = "请按组合键…" }
        override func keyDown(with event: NSEvent) {
            let flags = event.modifierFlags.intersection([.command, .option, .control, .shift])
            guard flags.contains(.command) || flags.contains(.option) || flags.contains(.control) else { NSSound.beep(); return }
            var modifiers: UInt32 = 0; var label = ""
            if flags.contains(.control) { modifiers |= UInt32(controlKey); label += "⌃" }
            if flags.contains(.option) { modifiers |= UInt32(optionKey); label += "⌥" }
            if flags.contains(.shift) { modifiers |= UInt32(shiftKey); label += "⇧" }
            if flags.contains(.command) { modifiers |= UInt32(cmdKey); label += "⌘" }
            label += event.keyCode == 49 ? "空格" : (event.charactersIgnoringModifiers ?? "").uppercased()
            onRecord?(.init(keyCode: UInt32(event.keyCode), modifiers: modifiers, label: label)); window?.makeFirstResponder(nil)
        }
    }
}

@MainActor final class SystemIntegration {
    private unowned let model: AppModel
    private var hotkeys: [EventHotKeyRef] = []
    private var handler: EventHandlerRef?
    private var copyMonitor: Any?
    private var lastCopy = -Double.infinity
    private var expiryTimer: Timer?
    let hover: HoverController
    let screenshot: ScreenshotCapture
    init(model: AppModel) {
        self.model = model; hover = HoverController(model: model); screenshot = ScreenshotCapture(model: model)
        var event = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, context in
            guard let event, let context else { return OSStatus(eventNotHandledErr) }
            var identifier = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil, MemoryLayout<EventHotKeyID>.size, nil, &identifier)
            guard status == noErr else { return status }
            let object = Unmanaged<SystemIntegration>.fromOpaque(context).takeUnretainedValue()
            Task { @MainActor in object.invoke(identifier.id) }
            return noErr
        }, 1, &event, Unmanaged.passUnretained(self).toOpaque(), &handler)
        copyMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in Task { @MainActor in self?.copyEvent(event) } }
        expiryTimer = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in Task { @MainActor in self?.model.checkExpiry(); await self?.model.refreshStubborn() } }
    }
    func configure() {
        for key in hotkeys { UnregisterEventHotKey(key) }; hotkeys = []
        var used: Set<String> = []
        for (index, name) in ["search", "clipboard", "screenshot"].enumerated() {
            guard let shortcut = model.preferences.shortcuts[name] else { continue }
            let identifier = "\(shortcut.keyCode)-\(shortcut.modifiers)"
            guard used.insert(identifier).inserted else { model.status = "快捷键存在重复，请在设置中修改。"; continue }
            var reference: EventHotKeyRef?
            let status = RegisterEventHotKey(shortcut.keyCode, shortcut.modifiers, EventHotKeyID(signature: 0x4D4C4558, id: UInt32(index + 1)), GetApplicationEventTarget(), 0, &reference)
            if status == noErr, let reference { hotkeys.append(reference) } else { model.status = "\(shortcut.label) 已被占用，请更换快捷键。" }
        }
        if (model.preferences.doubleCopy || model.preferences.hoverEnabled) && !PermissionCenter.accessibility { PermissionCenter.requestAccessibility() }
        hover.configure()
    }
    private func invoke(_ id: UInt32) {
        if id == 3, model.sheet == nil { screenshot.start(); return }
        model.showWindow?()
        guard model.sheet == nil else { return }
        if id == 1 { model.focusSearch?() }
        else if id == 2 { clipboard() }
    }
    func clipboard() { guard let text = NSPasteboard.general.string(forType: .string) else { return }; Task { await model.classify(text) } }
    private func copyEvent(_ event: NSEvent) {
        guard model.preferences.doubleCopy, model.sheet == nil, event.keyCode == 8, !event.isARepeat, event.modifierFlags.intersection([.command, .option, .control, .shift]) == .command else { return }
        if event.timestamp - lastCopy <= 0.75 {
            lastCopy = -Double.infinity
            Task { try? await Task.sleep(nanoseconds: 100_000_000); clipboard() }
        } else { lastCopy = event.timestamp }
    }
    func stop() {
        for key in hotkeys { UnregisterEventHotKey(key) }; hotkeys = []
        if let handler { RemoveEventHandler(handler) }
        if let copyMonitor { NSEvent.removeMonitor(copyMonitor) }; expiryTimer?.invalidate(); hover.stop(); screenshot.cancel()
    }
}

@MainActor enum MacUninstaller {
    private static var running = false
    static func remove(keepData: Bool, model: AppModel) {
        guard !running else { return }; running = true
        Task { await perform(keepData: keepData, model: model); running = false }
    }
    private static func perform(keepData: Bool, model: AppModel) async {
        do {
            let bundle = Bundle.main.bundleURL.resolvingSymlinksInPath()
            guard bundle.pathExtension == "app", Bundle.main.bundleIdentifier == "com.superraine.MemoLex" else { throw CoreFailure(code: "uninstall", message: "请从完整的墨墨搜词 App 中卸载。") }
            let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].resolvingSymlinksInPath()
            let profile = AppPaths.profile.standardizedFileURL
            let symbolic = (try? profile.resourceValues(forKeys: [.isSymbolicLinkKey]))?.isSymbolicLink == true
            guard profile.deletingLastPathComponent().resolvingSymlinksInPath() == support, profile.lastPathComponent == "MemoLex", !symbolic else { throw CoreFailure(code: "uninstall", message: "用户资料目录异常，已停止卸载。") }
            guard FileManager.default.isWritableFile(atPath: bundle.deletingLastPathComponent().path) else { throw CoreFailure(code: "uninstall", message: "无法移除当前位置的软件，请先将 App 移到应用程序文件夹，或手动移到废纸篓。") }
            try LoginService.apply(false)
            await model.core.shutDown()
            try FileManager.default.trashItem(at: bundle, resultingItemURL: nil)
            if !keepData {
                if FileManager.default.fileExists(atPath: profile.path) { try FileManager.default.removeItem(at: profile) }
                CredentialVault.removeAll(); UserDefaults.standard.removePersistentDomain(forName: "com.superraine.MemoLex")
            }
            model.removingApplication = true
            NSApp.terminate(nil)
        } catch { model.fail(error) }
    }
}
