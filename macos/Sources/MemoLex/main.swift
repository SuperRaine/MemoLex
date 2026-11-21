import AppKit
import SwiftUI

if CommandLine.arguments.count == 3, CommandLine.arguments[1] == "--apply-update" {
    do { try MacUpdateInstaller.apply(URL(fileURLWithPath: CommandLine.arguments[2])); exit(0) }
    catch { exit(1) }
}

private final class MemoLexWindow: NSWindow {
    override func toggleFullScreen(_ sender: Any?) {
        if !styleMask.contains(.fullScreen) { level = .normal }
        super.toggleFullScreen(sender)
    }
}

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    let model = AppModel()
    var window: NSWindow!
    var integration: SystemIntegration!
    private var statusItem: NSStatusItem!
    private var haiciWindow: NSWindow?
    private var haici: HaiciBrowser?
    private var forceQuit = false
    private var updatePending = false
    private var noticeShowing = false
    private var updateTimer: Timer?
    private var appliedStartAtLogin: Bool?
    private let windowedToolbar = NSToolbar(identifier: "MemoLex.Titlebar")
    private let loginRegistration = DispatchQueue(label: "MemoLex.LoginRegistration", qos: .utility)

    func applicationDidFinishLaunching(_ notification: Notification) {
        makeMenu()
        let visible = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1400, height: 900)
        window = MemoLexWindow(contentRect: NSRect(x: 0, y: 0, width: min(1160, visible.width - 40), height: min(800, visible.height - 45)), styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        window.collectionBehavior.insert(.fullScreenPrimary)
        window.title = "墨墨搜词"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.toolbarStyle = .unifiedCompact
        window.titlebarSeparatorStyle = .none
        window.toolbar = windowedToolbar
        window.isMovableByWindowBackground = true; window.isReleasedWhenClosed = false; window.delegate = self
        installContent(MainView(model: model), in: window)
        if !window.setFrameUsingName("MemoLex.Main") { window.center() }
        let minimumFrame = window.frameRect(forContentRect: CGRect(origin: .zero, size: MainView.minimumSize)).size
        window.minSize = minimumFrame
        window.contentMinSize = MainView.minimumSize
        let restored = window.frame
        if restored.width < minimumFrame.width || restored.height < minimumFrame.height {
            let height = max(restored.height, minimumFrame.height)
            window.setFrame(CGRect(x: restored.minX, y: restored.maxY - height,
                                   width: max(restored.width, minimumFrame.width), height: height), display: false)
        }
        window.setFrameAutosaveName("MemoLex.Main")
        model.showWindow = { [weak self] in self?.show() }
        model.openHaici = { [weak self] word in self?.openHaici(word) }
        model.applySystemPreferences = { [weak self] in self?.applyPreferences() }
        model.presentNotice = { [weak self] in DispatchQueue.main.async { self?.presentNotice() } }
        integration = SystemIntegration(model: model)
        model.captureScreen = { [weak self] in self?.integration.screenshot.start() }
        makeStatusItem(); applyPreferences()
        model.updates.onAvailable = { [weak self] in self?.updatePending = true; self?.promptUpdate() }
        updateTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in Task { @MainActor in self?.promptUpdate() } }
        show()
        model.updates.start()
        Task { await model.initialize() }
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { show(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationWillTerminate(_ notification: Notification) { if !model.removingApplication { model.preferences.save() }; integration?.stop(); model.updates.stop(); model.core.stop(); updateTimer?.invalidate() }
    func show() {
        guard let window else { return }
        NSApp.setActivationPolicy(.regular)
        if window.isMiniaturized { window.deminiaturize(nil) }
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        DispatchQueue.main.async { [weak self] in self?.alignTitlebar() }
    }

    func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize {
        guard sender === window else { return frameSize }
        return NSSize(width: max(frameSize.width, sender.minSize.width, MainView.minimumSize.width),
                      height: max(frameSize.height, sender.minSize.height, MainView.minimumSize.height))
    }
    func windowDidResize(_ notification: Notification) { alignTitlebar() }
    func windowWillEnterFullScreen(_ notification: Notification) {
        window.level = .normal
        window.toolbar = nil
    }
    func windowDidEnterFullScreen(_ notification: Notification) { alignTitlebar(); applyWindowLevel() }
    func windowDidExitFullScreen(_ notification: Notification) { restoreWindowedTitlebar() }
    func windowDidFailToEnterFullScreen(_ window: NSWindow) { restoreWindowedTitlebar() }
    func windowDidChangeScreen(_ notification: Notification) { alignTitlebar() }

    private func installContent<Content: View>(_ view: Content, in window: NSWindow) {
        // The native container owns the window limits. A root hosting view can reset them.
        let container = NSView(frame: .zero)
        window.contentView = container
        let hosting = NSHostingView(rootView: view)
        hosting.sizingOptions = []
        hosting.frame = container.bounds
        hosting.autoresizingMask = [.width, .height]
        container.addSubview(hosting)
    }

    private func alignTitlebar() {
        guard let window, let content = window.contentView else { return }
        let next: TitlebarMetrics
        let contentRect = content.convert(content.bounds, to: nil)
        if window.styleMask.contains(.fullScreen) {
            next = TitlebarMetrics(leading: 14, center: 20, isFullScreen: true)
        } else {
            let buttons: [NSWindow.ButtonType] = [.closeButton, .miniaturizeButton, .zoomButton]
            let frames = buttons.compactMap { type -> CGRect? in
                guard let button = window.standardWindowButton(type), button.superview != nil else { return nil }
                return button.convert(button.bounds, to: nil)
            }
            guard let first = frames.first, let right = frames.map(\.maxX).max() else { return }
            let center = contentRect.maxY - first.midY
            guard (10...48).contains(center) else { return }
            next = TitlebarMetrics(leading: right - contentRect.minX + 10, center: center)
        }
        if model.titlebar != next { model.titlebar = next }
    }

    private func restoreWindowedTitlebar() {
        window.toolbar = windowedToolbar
        applyWindowLevel()
        DispatchQueue.main.async { [weak self] in self?.alignTitlebar() }
    }
    private func applyWindowLevel() {
        guard let window else { return }
        let floating = model.preferences.alwaysOnTop && !window.styleMask.contains(.fullScreen)
        let level: NSWindow.Level = floating ? .floating : .normal
        if window.level != level { window.level = level }
    }
    func applyPreferences() {
        applyWindowLevel()
        integration?.configure()
        let startAtLogin = model.preferences.startAtLogin
        guard appliedStartAtLogin != startAtLogin else { return }
        appliedStartAtLogin = startAtLogin
        loginRegistration.async { [weak self] in
            do { try LoginService.apply(startAtLogin) }
            catch {
                DispatchQueue.main.async {
                    guard let self, self.model.preferences.startAtLogin == startAtLogin else { return }
                    self.model.status = "开机启动设置未完成，请在系统设置 → 通用 → 登录项中检查。"
                }
            }
        }
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        guard sender === window else { return true }
        if forceQuit { return true }
        if model.preferences.rememberClose { if model.preferences.closeToTray { sender.orderOut(nil) } else { quit() }; return false }
        let alert = NSAlert(); alert.messageText = "关闭墨墨搜词"; alert.informativeText = "你希望退出软件，还是收起到菜单栏？"
        alert.addButton(withTitle: "退出软件"); alert.addButton(withTitle: "收起到菜单栏"); alert.addButton(withTitle: "取消")
        let remember = NSButton(checkboxWithTitle: "记住选择，下次不再询问", target: nil, action: nil); remember.state = .off; alert.accessoryView = remember
        let choice = alert.runModal(); guard choice != .alertThirdButtonReturn else { return false }
        model.preferences.closeToTray = choice == .alertSecondButtonReturn; model.preferences.rememberClose = remember.state == .on; model.preferences.save()
        if model.preferences.closeToTray { sender.orderOut(nil) } else { quit() }; return false
    }
    @objc func quit() { forceQuit = true; NSApp.terminate(nil) }
    @objc func showMain() { show(); model.focusSearch?() }
    @objc func settings() { show(); model.sheet = .settings }
    @objc func history() { show(); model.sheet = .history }
    @objc func clipboard() { show(); integration.clipboard() }
    @objc func screenshot() { integration.screenshot.start() }
    @objc func translate() { show(); model.enterTranslation() }
    @objc func back() { model.move(-1) }
    @objc func forward() { model.move(1) }
    @objc func primaryAction() { guard model.sheet == nil, model.tourIndex == nil else { return }; if model.page == .translation { Task { await model.translate() } } else { Task { await model.toggleMembership() } } }
    private func makeMenu() {
        let menu = NSMenu(); NSApp.mainMenu = menu
        let app = NSMenu(); let appItem = NSMenuItem(); appItem.submenu = app; menu.addItem(appItem)
        add(app, "关于墨墨搜词", #selector(about), ""); app.addItem(.separator()); add(app, "设置…", #selector(settings), ","); app.addItem(.separator())
        add(app, "隐藏墨墨搜词", #selector(NSApplication.hide(_:)), "h", target: NSApp); add(app, "隐藏其他", #selector(NSApplication.hideOtherApplications(_:)), "h", modifiers: [.command, .option], target: NSApp); add(app, "显示全部", #selector(NSApplication.unhideAllApplications(_:)), "", target: NSApp); app.addItem(.separator()); add(app, "退出墨墨搜词", #selector(quit), "q")
        let edit = NSMenu(title: "编辑"); let editItem = NSMenuItem(title: "编辑", action: nil, keyEquivalent: ""); editItem.submenu = edit; menu.addItem(editItem)
        for (title, action, key) in [("撤销", "undo:", "z"), ("重做", "redo:", "Z"), ("剪切", "cut:", "x"), ("复制", "copy:", "c"), ("粘贴", "paste:", "v"), ("全选", "selectAll:", "a")] { edit.addItem(NSMenuItem(title: title, action: Selector(action), keyEquivalent: key)) }
        let lookup = NSMenu(title: "查词"); let lookupItem = NSMenuItem(title: "查词", action: nil, keyEquivalent: ""); lookupItem.submenu = lookup; menu.addItem(lookupItem)
        add(lookup, "快速查词", #selector(showMain), "l"); add(lookup, "查词历史", #selector(history), "y"); add(lookup, "段落翻译", #selector(translate), "t"); add(lookup, "查询剪贴板", #selector(clipboard), ""); add(lookup, "截图翻译", #selector(screenshot), ""); lookup.addItem(.separator()); add(lookup, "后退", #selector(back), "["); add(lookup, "前进", #selector(forward), "]"); add(lookup, "收藏单词 / 翻译", #selector(primaryAction), "\r")
    }
    private func add(_ menu: NSMenu, _ title: String, _ action: Selector, _ key: String, modifiers: NSEvent.ModifierFlags = .command, target: AnyObject? = nil) { let item = NSMenuItem(title: title, action: action, keyEquivalent: key); item.keyEquivalentModifierMask = modifiers; item.target = target ?? self; menu.addItem(item) }
    @objc func about() { show(); model.settingsPage = "关于与赞助"; model.sheet = .settings }
    private func makeStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let image = NSImage(contentsOf: AppPaths.resources.appendingPathComponent("owl-logo.png")) { image.size = NSSize(width: 21, height: 21); statusItem.button?.image = image } else { statusItem.button?.title = "墨" }
        let menu = NSMenu(); add(menu, "打开墨墨搜词", #selector(showMain), ""); add(menu, "查询剪贴板", #selector(clipboard), ""); add(menu, "截图翻译", #selector(screenshot), ""); add(menu, "设置…", #selector(settings), ""); menu.addItem(.separator()); add(menu, "退出", #selector(quit), ""); statusItem.menu = menu
    }
    private func openHaici(_ word: String) {
        if let haiciWindow, let haici { haici.navigate(word: word); haiciWindow.makeKeyAndOrderFront(nil); return }
        let browser = HaiciBrowser(word: word); let browserWindow = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1000, height: 720), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        browserWindow.title = "海词词典"
        browserWindow.isReleasedWhenClosed = false
        installContent(HaiciView(browser: browser), in: browserWindow)
        browserWindow.contentMinSize = NSSize(width: 600, height: 450)
        browserWindow.center(); browserWindow.makeKeyAndOrderFront(nil); haici = browser; haiciWindow = browserWindow
    }
    private func promptUpdate() {
        guard updatePending, window?.isVisible == true, model.sheet == nil, model.tourIndex == nil, model.notice == nil, NSApp.modalWindow == nil else { return }
        updatePending = false
        let alert = NSAlert()
        alert.messageText = "发现新版 \(model.updates.latestVersion)"
        alert.informativeText = model.updates.downloaded != nil ? "新版已下载，可前往设置安装。" : "可以现在更新，也可以稍后在设置中查看。"
        let action = !model.updates.hasUpdate ? "前往下载页面" : model.updates.busy || model.updates.downloaded != nil ? "查看更新" : "下载更新"
        alert.addButton(withTitle: action)
        alert.addButton(withTitle: "稍后")
        alert.accessoryView = NSHostingView(rootView: UpdatePromptOptions(preferences: model.preferences).frame(width: 290, height: 32))
        alert.beginSheetModal(for: window) { [weak self] response in
            Task { @MainActor in
                guard let self else { return }
                self.model.preferences.save()
                guard response == .alertFirstButtonReturn else { return }
                if !self.model.updates.hasUpdate {
                    if let url = URL(string: AppPaths.repository + "/releases/latest") { NSWorkspace.shared.open(url) }
                    return
                }
                self.model.settingsPage = "软件更新"
                self.model.sheet = .settings
                if !self.model.updates.busy && self.model.updates.downloaded == nil { await self.model.updates.download() }
            }
        }
    }
    private func presentNotice() {
        guard !noticeShowing, let notice = model.notice, let parent = NSApp.keyWindow ?? window else { return }
        noticeShowing = true
        let alert = NSAlert(); alert.messageText = notice.title; alert.informativeText = notice.message
        alert.alertStyle = notice.destructive ? .warning : .informational
        alert.addButton(withTitle: notice.confirm ?? "知道了"); if notice.confirm != nil { alert.addButton(withTitle: "取消") }
        alert.beginSheetModal(for: parent) { [weak self] response in Task { @MainActor in
            guard let self else { return }; self.noticeShowing = false
            if self.model.notice?.id == notice.id { self.model.notice = nil }
            if response == .alertFirstButtonReturn { notice.action?() }
            if self.model.notice != nil { self.presentNotice() }
        } }
    }
}
private struct UpdatePromptOptions: View { @ObservedObject var preferences: Preferences; var body: some View { Toggle("以后发现新版时自动下载", isOn: $preferences.automaticDownloads).toggleStyle(.switch).controlSize(.small) } }

MainActor.assumeIsolated {
    let application = NSApplication.shared
    let delegate = AppDelegate()
    application.delegate = delegate
    application.setActivationPolicy(.regular)
    application.run()
}
