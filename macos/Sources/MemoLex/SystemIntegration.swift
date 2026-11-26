import AppKit
import ApplicationServices
import Carbon
import Foundation
import ServiceManagement
import SwiftUI

@MainActor enum PermissionCenter {
    enum Kind: String {
        case accessibility = "Privacy_Accessibility"
        case screen = "Privacy_ScreenCapture"

        var title: String { self == .accessibility ? "辅助功能" : "屏幕录制" }
        var icon: String { self == .accessibility ? "hand.tap" : "rectangle.dashed" }
        var service: String { self == .accessibility ? "Accessibility" : "ScreenCapture" }
    }
    struct Snapshot: Equatable {
        var accessibility = false
        var screen = false

        func allows(_ kind: Kind) -> Bool { kind == .accessibility ? accessibility : screen }
    }
    private static var cached = Snapshot()
    private static var lastProbe = -Double.infinity
    private static var uptime: TimeInterval { ProcessInfo.processInfo.systemUptime }

    static var accessibility: Bool { AXIsProcessTrusted() || snapshot().accessibility }
    static var screen: Bool { CGPreflightScreenCaptureAccess() }

    static func snapshot(refresh: Bool = false) -> Snapshot {
        if refresh || uptime - lastProbe >= 2 {
            cached = Snapshot(accessibility: AXIsProcessTrusted() || canReadOtherApplication(),
                              screen: screen)
            lastProbe = uptime
        }
        return cached
    }

    private static func canReadOtherApplication() -> Bool {
        let ownPID = ProcessInfo.processInfo.processIdentifier
        let applications = NSWorkspace.shared.runningApplications.filter {
            $0.processIdentifier != ownPID && $0.activationPolicy == .regular && !$0.isTerminated
        }
        return applications.prefix(3).contains { application in
            let element = AXUIElementCreateApplication(application.processIdentifier)
            AXUIElementSetMessagingTimeout(element, 0.10)
            var role: CFTypeRef?
            return AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &role) == .success && role != nil
        }
    }

    static func openSettings(for kind: Kind) {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(kind.rawValue)")!
        NSWorkspace.shared.open(url)
    }

    static func resetAuthorization(for kind: Kind) async throws -> Bool {
        guard !snapshot(refresh: true).allows(kind) else { return false }
        let failure = CoreFailure(code: "permissions", message: "无法重置\(kind.title)授权。")
        let bundleID = "com.superraine.MemoLex"
        guard Bundle.main.bundleIdentifier == bundleID, Bundle.main.bundleURL.pathExtension == "app" else { throw failure }
        let service = kind.service
        try await Task.detached(priority: .utility) {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/tccutil")
            process.arguments = ["reset", service, bundleID]
            process.standardInput = FileHandle.nullDevice
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            try process.run()
            process.waitUntilExit()
            guard process.terminationStatus == 0 else { throw failure }
        }.value
        return true
    }
}

struct PermissionsSettings: View {
    @ObservedObject var model: AppModel
    @State private var permissions = PermissionCenter.Snapshot()
    @State private var resetting: PermissionCenter.Kind?
    @State private var message = ""
    var body: some View {
        VStack(alignment: .leading, spacing: 13) {
            permission(.accessibility, enabled: permissions.accessibility,
                       detail: "读取其他软件中的英文文本，监听连续两次 ⌘C。")
            permission(.screen, enabled: permissions.screen,
                       detail: "用于图片取词和截图翻译。识别在本机完成。")
            HStack(spacing: 10) {
                Button(action: refresh) { Label("刷新权限状态", systemImage: "arrow.clockwise") }
                Button("在访达中显示软件") {
                    NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL.resolvingSymlinksInPath()])
                }
            }.buttonStyle(MemoButtonStyle())
            if !message.isEmpty || !permissions.accessibility || !permissions.screen {
                Text(message.isEmpty ? "用「＋」添加当前 App。若刚开启权限，请退出并重新打开墨墨搜词。更新后仍无法授权时，可以重置旧授权。" : message)
                    .font(.callout).foregroundStyle(Palette.muted).fixedSize(horizontal: false, vertical: true)
            }
        }.tourTarget("permissions")
            .onAppear(perform: refresh)
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in refresh() }
    }
    private func refresh() {
        permissions = PermissionCenter.snapshot(refresh: true)
        model.applySystemPreferences?()
    }
    private func permission(_ kind: PermissionCenter.Kind, enabled: Bool, detail: String) -> some View {
        SettingsCard(title: kind.title) {
            HStack {
                Label(enabled ? "已允许" : "待系统确认", systemImage: enabled ? "checkmark.circle.fill" : kind.icon)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(enabled ? Color(red: 0.06, green: 0.56, blue: 0.37) : Color(red: 0.75, green: 0.44, blue: 0.04))
                    .padding(.horizontal, 10).padding(.vertical, 4)
                    .background((enabled ? Color(red: 0.08, green: 0.72, blue: 0.48) : Color(red: 0.96, green: 0.62, blue: 0.04)).opacity(0.14), in: Capsule())
                Spacer()
                if !enabled {
                    Button(resetting == kind ? "正在重置…" : "重置旧授权") { confirmReset(kind) }
                        .buttonStyle(MemoButtonStyle(compact: true)).disabled(resetting != nil)
                }
                Button("前往系统设置") { PermissionCenter.openSettings(for: kind) }
                    .buttonStyle(MemoButtonStyle(accent: !enabled, compact: true)).disabled(resetting != nil)
            }
            Text(detail).font(.callout).foregroundStyle(Palette.muted)
        }
    }

    private func confirmReset(_ kind: PermissionCenter.Kind) {
        model.notice = .init(title: "重置\(kind.title)授权？",
                             message: "重置后，需要在系统设置里为当前版本开启这项权限。若刚授权，请先退出并重新打开墨墨搜词。",
                             confirm: "重置旧授权", action: { Task { await reset(kind) } })
    }

    @MainActor private func reset(_ kind: PermissionCenter.Kind) async {
        guard resetting == nil else { return }
        resetting = kind
        defer { resetting = nil }
        do {
            guard try await PermissionCenter.resetAuthorization(for: kind) else {
                refresh()
                message = "\(kind.title)已允许。"
                return
            }
            message = "旧授权已清除。请在系统设置中用「＋」添加当前的墨墨搜词，再开启\(kind.title)。"
        } catch {
            message = "无法重置\(kind.title)授权。请在系统设置里用「−」移除旧条目，再用「＋」添加当前版本。"
        }
        refresh()
        PermissionCenter.openSettings(for: kind)
    }
}

enum LoginService {
    static func apply(_ enabled: Bool) throws {
        let app = SMAppService.mainApp
        let status = app.status
        if enabled && status != .enabled && status != .requiresApproval { try app.register() }
        if !enabled && (status == .enabled || status == .requiresApproval) { try app.unregister() }
    }
}

struct ShortcutSettings: View {
    @ObservedObject var preferences: Preferences
    private let rows = [("search", "唤起快速查词"), ("clipboard", "查询剪贴板"), ("screenshot", "截图翻译")]
    var body: some View {
        VStack(alignment: .leading, spacing: 15) {
            SettingsCard(title: "全局快捷键") {
                Grid(alignment: .leading, horizontalSpacing: 14, verticalSpacing: 10) {
                    ForEach(rows, id: \.0) { key, title in
                        GridRow {
                            Text(title).font(.callout)
                            ShortcutRecorder(shortcut: Binding(get: { preferences.shortcuts[key] ?? NativeShortcut.defaults[key]! },
                                                               set: { preferences.shortcuts[key] = $0 }))
                                .frame(width: 145, height: 33).controlSurface().accessibilityLabel(title)
                        }
                    }
                }
                Text("全局快捷键可在其他软件中使用。").font(.callout).foregroundStyle(Palette.muted)
            }
            SettingsCard(title: "窗口内快捷键") {
                Text("⌘L 快速查词   ·   ⌘Y 查词历史   ·   ⌘[ / ⌘] 后退或前进").foregroundStyle(Palette.muted)
                Text("⌘T 段落翻译   ·   ⌘↩ 加入云词本或开始翻译").foregroundStyle(Palette.muted)
            }
        }
    }
}

private struct ShortcutRecorder: NSViewRepresentable {
    @Binding var shortcut: NativeShortcut
    func makeNSView(context: Context) -> KeyField {
        let field = KeyField()
        field.onRecord = { shortcut = $0 }
        field.stringValue = shortcut.label
        field.isEditable = false
        field.isBezeled = false
        field.isBordered = false
        field.drawsBackground = false
        field.focusRingType = .none
        field.font = .monospacedSystemFont(ofSize: 13, weight: .medium)
        field.alignment = .center
        return field
    }
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
    private var installedShortcuts: [String: NativeShortcut]?
    private var hoverEnabled: Bool?
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
        expiryTimer = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in Task { @MainActor in self?.ensureCopyMonitor(); self?.model.checkExpiry(); await self?.model.refreshStubborn() } }
    }
    func configure() {
        let preferences = model.preferences
        if installedShortcuts != preferences.shortcuts {
            installShortcuts(preferences.shortcuts)
            installedShortcuts = preferences.shortcuts
        }
        let enabledHover = preferences.hoverEnabled
        if enabledHover != hoverEnabled { hover.configure() }
        hoverEnabled = enabledHover
        ensureCopyMonitor()
    }
    private func installShortcuts(_ shortcuts: [String: NativeShortcut]) {
        for key in hotkeys { UnregisterEventHotKey(key) }
        hotkeys = []
        var used: Set<String> = []
        for (index, name) in ["search", "clipboard", "screenshot"].enumerated() {
            guard let shortcut = shortcuts[name] else { continue }
            let identifier = "\(shortcut.keyCode)-\(shortcut.modifiers)"
            guard used.insert(identifier).inserted else { model.status = "快捷键存在重复，请在设置中修改。"; continue }
            var reference: EventHotKeyRef?
            let status = RegisterEventHotKey(shortcut.keyCode, shortcut.modifiers, EventHotKeyID(signature: 0x4D4C4558, id: UInt32(index + 1)), GetApplicationEventTarget(), 0, &reference)
            if status == noErr, let reference { hotkeys.append(reference) } else { model.status = "\(shortcut.label) 已被占用，请更换快捷键。" }
        }
    }
    private func ensureCopyMonitor() {
        if model.preferences.doubleCopy && PermissionCenter.accessibility {
            if copyMonitor == nil {
                copyMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
                    Task { @MainActor in self?.copyEvent(event) }
                }
            }
        } else if let copyMonitor {
            NSEvent.removeMonitor(copyMonitor)
            self.copyMonitor = nil
        }
    }
    private func invoke(_ id: UInt32) {
        if id == 3, model.sheet == nil { screenshot.start(); return }
        if id == 1, model.sheet == nil, model.preferences.useQuickLookupBar { model.showQuickLookup?(); return }
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
                UserDefaults.standard.removePersistentDomain(forName: "com.superraine.MemoLex")
            }
            model.removingApplication = true
            NSApp.terminate(nil)
        } catch { model.fail(error) }
    }
}
