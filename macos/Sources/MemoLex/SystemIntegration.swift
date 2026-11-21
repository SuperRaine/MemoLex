import AppKit
import ApplicationServices
import Carbon
import Foundation
import ServiceManagement
import SwiftUI

@MainActor enum PermissionCenter {
    enum Kind: String, Sendable {
        case accessibility = "Accessibility"
        case screen = "ScreenCapture"

        var title: String { self == .accessibility ? "辅助功能" : "屏幕录制" }
        var icon: String { self == .accessibility ? "hand.tap" : "rectangle.dashed" }
    }
    struct Snapshot: Equatable {
        var accessibility = false
        var screen = false
    }
    private static var cached = Snapshot()
    private static var lastProbe = -Double.infinity
    private static var lastCapture = -Double.infinity
    private static var uptime: TimeInterval { ProcessInfo.processInfo.systemUptime }

    static var accessibility: Bool { AXIsProcessTrusted() || snapshot().accessibility }
    static var screen: Bool { CGPreflightScreenCaptureAccess() || uptime - lastCapture < 10 || snapshot().screen }

    static func snapshot(refresh: Bool = false) -> Snapshot {
        if refresh || uptime - lastProbe >= 2 {
            cached = Snapshot(accessibility: AXIsProcessTrusted() || canReadOtherApplication(),
                              screen: CGPreflightScreenCaptureAccess() || canReadOtherWindowTitles())
            lastProbe = uptime
        }
        return Snapshot(accessibility: cached.accessibility, screen: cached.screen || uptime - lastCapture < 10)
    }

    static func recordScreenAccess() { lastCapture = uptime }

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

    private static func canReadOtherWindowTitles() -> Bool {
        let ownPID = ProcessInfo.processInfo.processIdentifier
        let pids = Set(NSWorkspace.shared.runningApplications.filter { $0.activationPolicy == .regular && $0.processIdentifier != ownPID }.map(\.processIdentifier))
        guard !pids.isEmpty,
              let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { return false }
        // Quartz withholds other applications' window titles when recording is unauthorized.
        return windows.contains { window in
            guard let pid = window[kCGWindowOwnerPID as String] as? NSNumber, pids.contains(pid.int32Value),
                  let title = window[kCGWindowName as String] as? String else { return false }
            return !title.isEmpty
        }
    }

    static func requestAccessibility() { _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary) }
    static func requestScreen() { _ = CGRequestScreenCaptureAccess() }
    static func open(_ section: String) { if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(section)") { NSWorkspace.shared.open(url) } }

    static func openSettings(for kind: Kind, request: Bool) {
        if request {
            switch kind {
            case .accessibility: requestAccessibility()
            case .screen: requestScreen()
            }
        }
        open("Privacy_\(kind.rawValue)")
    }

    static func verify() async -> Snapshot {
        let status = snapshot(refresh: true)
        if !status.screen, await ScreenCapture.hasAccess() { recordScreenAccess() }
        return snapshot()
    }

    static func reset(_ kind: Kind) async -> Bool {
        guard let bundleID = Bundle.main.bundleIdentifier, bundleID == "com.superraine.MemoLex",
              Bundle.main.bundleURL.pathExtension == "app" else { return false }
        let success = await Task.detached(priority: .utility) {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/tccutil")
            process.arguments = ["reset", kind.rawValue, bundleID]
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            do {
                try process.run()
                process.waitUntilExit()
                return process.terminationStatus == 0
            } catch { return false }
        }.value
        if success {
            lastProbe = -.infinity
            if kind == .screen { lastCapture = -.infinity }
        }
        return success
    }
}

struct PermissionsSettings: View {
    @ObservedObject var model: AppModel
    @State private var refresh = 0
    @State private var permissions: PermissionCenter.Snapshot?
    @State private var checking = false
    @State private var resetting: PermissionCenter.Kind?
    @State private var recovery = ""
    var body: some View {
        VStack(alignment: .leading, spacing: 13) {
            permission(.accessibility, enabled: permissions?.accessibility,
                       detail: "读取其他软件中的英文文本，监听连续两次 ⌘C。")
            permission(.screen, enabled: permissions?.screen,
                       detail: "用于图片取词和截图翻译。识别在本机完成。")
            HStack(spacing: 10) {
                Button {
                    checking = true
                    Task {
                        permissions = await PermissionCenter.verify()
                        model.applySystemPreferences?()
                        checking = false
                    }
                } label: {
                    Label(checking ? "正在检查…" : "刷新权限状态", systemImage: "arrow.clockwise")
                }.disabled(checking || resetting != nil)
                Button("在访达中显示软件") {
                    NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL.resolvingSymlinksInPath()])
                }
            }
            .buttonStyle(MemoButtonStyle())
            if !recovery.isEmpty { Text(recovery).font(.callout).foregroundStyle(Palette.muted).fixedSize(horizontal: false, vertical: true) }
        }.tourTarget("permissions")
            .task(id: refresh) {
                permissions = PermissionCenter.snapshot(refresh: true)
                model.applySystemPreferences?()
                try? await Task.sleep(nanoseconds: 600_000_000)
                if !Task.isCancelled { permissions = PermissionCenter.snapshot(refresh: true) }
            }
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in refresh += 1 }
    }
    private func permission(_ kind: PermissionCenter.Kind, enabled: Bool?, detail: String) -> some View {
        SettingsCard(title: kind.title) {
            HStack {
                Label(enabled == true ? "已允许" : enabled == nil ? "正在检查…" : "待系统确认",
                      systemImage: enabled == true ? "checkmark.circle.fill" : kind.icon)
                    .font(.callout).foregroundStyle(enabled == true ? Palette.deep : Palette.muted)
                Spacer()
                HStack(spacing: 7) {
                    Button("前往系统设置") { PermissionCenter.openSettings(for: kind, request: enabled != true) }
                    Button(resetting == kind ? "正在重置…" : "重新授权") { confirmReset(kind) }
                }.buttonStyle(MemoButtonStyle(compact: true)).disabled(resetting != nil || checking)
            }
            Text(detail).font(.callout).foregroundStyle(Palette.muted)
        }
    }

    private func confirmReset(_ kind: PermissionCenter.Kind) {
        model.notice = .init(title: "重新授权\(kind.title)？",
                             message: "清除墨墨搜词的这项旧授权，然后在系统设置中允许当前版本。",
                             confirm: "重新授权", action: {
            Task {
                resetting = kind
                let success = await PermissionCenter.reset(kind)
                resetting = nil
                recovery = success
                    ? "旧授权已重置。请在系统设置允许墨墨搜词，再退出并重新打开软件。"
                    : "请在系统设置中移除旧的墨墨搜词条目，用「＋」添加当前 App 并开启权限，再重新打开软件。"
                PermissionCenter.openSettings(for: kind, request: success)
                refresh += 1
            }
        })
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
    private var doubleCopyEnabled: Bool?
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
        let enabledCopy = preferences.doubleCopy
        if ((hoverEnabled == false && enabledHover) || (doubleCopyEnabled == false && enabledCopy)) && !PermissionCenter.accessibility {
            PermissionCenter.requestAccessibility()
        }
        if hoverEnabled == false && enabledHover && !PermissionCenter.screen { PermissionCenter.requestScreen() }
        if !enabledCopy, let copyMonitor { NSEvent.removeMonitor(copyMonitor); self.copyMonitor = nil }
        if enabledHover != hoverEnabled { hover.configure() }
        hoverEnabled = enabledHover
        doubleCopyEnabled = enabledCopy
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
    private func ensureCopyMonitor() { if model.preferences.doubleCopy, PermissionCenter.accessibility, copyMonitor == nil { copyMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in Task { @MainActor in self?.copyEvent(event) } } } }
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
