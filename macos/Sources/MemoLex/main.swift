import AppKit
import SwiftUI

if CommandLine.arguments.count == 3, CommandLine.arguments[1] == "--apply-update" {
    do { try MacUpdateInstaller.apply(URL(fileURLWithPath: CommandLine.arguments[2])); exit(0) }
    catch { exit(1) }
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
    private var updateTimer: Timer?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.appearance = NSAppearance(named: .aqua)
        makeMenu()
        let visible = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1400, height: 900)
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: min(1160, visible.width - 40), height: min(800, visible.height - 45)), styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        window.title = "墨墨搜词"; window.titleVisibility = .hidden; window.titlebarAppearsTransparent = true; window.toolbar = nil
        window.standardWindowButton(.closeButton)?.isHidden = true; window.standardWindowButton(.miniaturizeButton)?.isHidden = true; window.standardWindowButton(.zoomButton)?.isHidden = true
        window.minSize = NSSize(width: 760, height: 520); window.isMovableByWindowBackground = true; window.isReleasedWhenClosed = false; window.delegate = self
        window.contentView = NSHostingView(rootView: MainView(model: model).preferredColorScheme(.light)); window.setFrameAutosaveName("MemoLex.Main"); window.center()
        model.showWindow = { [weak self] in self?.show() }
        model.openHaici = { [weak self] word in self?.openHaici(word) }
        model.applySystemPreferences = { [weak self] in self?.applyPreferences() }
        integration = SystemIntegration(model: model)
        model.captureScreen = { [weak self] in self?.integration.screenshot.start() }
        makeStatusItem(); applyPreferences()
        model.updates.onAvailable = { [weak self] in self?.updatePending = true; self?.promptUpdate() }
        updateTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in Task { @MainActor in self?.promptUpdate() } }
        show(); Task { await model.initialize() }
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { show(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationWillTerminate(_ notification: Notification) { model.preferences.save(); integration?.stop(); model.updates.stop(); model.core.stop(); updateTimer?.invalidate() }
    func show() { guard let window else { return }; NSApp.setActivationPolicy(.regular); if window.isMiniaturized { window.deminiaturize(nil) }; window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    func applyPreferences() {
        window?.level = model.preferences.alwaysOnTop ? .floating : .normal
        do { try LoginService.apply(model.preferences.startAtLogin) } catch { model.status = "开机启动设置未完成，请在系统设置 → 通用 → 登录项中检查。" }
        integration?.configure()
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
    @objc func primaryAction() { if model.page == .translation { Task { await model.translate() } } else { Task { await model.toggleMembership() } } }
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
        browserWindow.title = "海词词典"; browserWindow.minSize = NSSize(width: 600, height: 450); browserWindow.isReleasedWhenClosed = false; browserWindow.contentView = NSHostingView(rootView: HaiciView(browser: browser, close: { [weak browserWindow] in browserWindow?.close() }).preferredColorScheme(.light)); browserWindow.center(); browserWindow.makeKeyAndOrderFront(nil); haici = browser; haiciWindow = browserWindow
    }
    private func promptUpdate() {
        guard updatePending, window?.isVisible == true, model.sheet == nil, model.tourIndex == nil, model.notice == nil, NSApp.modalWindow == nil else { return }
        updatePending = false
        let alert = NSAlert(); alert.messageText = "发现新版 \(model.updates.latestVersion)"; alert.informativeText = model.updates.downloaded != nil ? "新版已下载完成，可前往设置安装。" : "可以现在下载，也可以稍后在设置中更新。"
        alert.addButton(withTitle: model.updates.busy || model.updates.downloaded != nil ? "查看更新" : "下载更新"); alert.addButton(withTitle: "稍后")
        alert.accessoryView = NSHostingView(rootView: UpdatePromptOptions(preferences: model.preferences).frame(width: 290, height: 32))
        alert.beginSheetModal(for: window) { [weak self] response in Task { @MainActor in guard let self else { return }; self.model.preferences.save(); if response == .alertFirstButtonReturn { self.model.settingsPage = "软件更新"; self.model.sheet = .settings; if !self.model.updates.busy && self.model.updates.downloaded == nil { await self.model.updates.download() } } } }
    }
}
private struct UpdatePromptOptions: View { @ObservedObject var preferences: Preferences; var body: some View { Toggle("以后发现新版时自动下载", isOn: $preferences.automaticDownloads).toggleStyle(.switch).controlSize(.small) } }

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.setActivationPolicy(.regular)
application.run()
