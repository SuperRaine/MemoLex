import AppKit
import SwiftUI
import WebKit

@MainActor final class LoginBrowser: NSObject, ObservableObject, WKNavigationDelegate, WKScriptMessageHandler {
    let browser: WKWebView
    @Published var status = "正在打开墨墨官方登录页…"
    @Published var busy = false
    @Published var showsLoginForm = false
    private unowned let model: AppModel
    private var identity = ""
    private var completion: Task<Void, Never>?
    private var finishedCapture: (String, URL)?
    private let startURL = URL(string: "https://open.maimemo.com/open/api/v1/tokens/openapi")!
    init(model: AppModel) {
        self.model = model
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        let controller = WKUserContentController()
        controller.addUserScript(WKUserScript(source: #"""
        (() => {
          if (location.protocol !== 'https:' || location.hostname !== 'accounts.maimemo.com' || !location.pathname.startsWith('/interaction/')) return;
          const selector = 'input[name="identity"][autocomplete="username"]';
          const report = (field) => {
            if (!field?.matches(selector)) return;
            const value = field.value.trim();
            if (value.length > 0 && value.length < 200) window.webkit.messageHandlers.MemoLexIdentity.postMessage(value);
          };
          document.addEventListener('change', (event) => report(event.target), true);
          document.addEventListener('submit', (event) => report(event.target.querySelector(selector)), true);
        })();
        """#, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        controller.addUserScript(WKUserScript(source: #"""
        (() => {
          if (location.protocol !== 'https:' || location.hostname !== 'open.maimemo.com' || location.pathname !== '/open/api/v1/tokens/openapi') return;
          const content = document.querySelector('body > pre')?.textContent || document.documentElement.outerHTML;
          if (content.length <= 65536) window.webkit.messageHandlers.MemoLexAuthorization.postMessage(content);
        })();
        """#, injectionTime: .atDocumentEnd, forMainFrameOnly: true))
        configuration.userContentController = controller
        browser = WKWebView(frame: .zero, configuration: configuration)
        super.init()
        controller.add(self, name: "MemoLexIdentity")
        controller.add(self, name: "MemoLexAuthorization")
        browser.navigationDelegate = self
        browser.load(URLRequest(url: startURL))
    }
    func retry() {
        guard completion == nil else { return }
        browser.stopLoading()
        identity = ""
        showsLoginForm = false
        status = "正在打开墨墨官方登录页…"
        browser.configuration.websiteDataStore.removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), modifiedSince: .distantPast) { [weak self] in
            Task { @MainActor in
                guard let self, self.model.showLogin else { return }
                self.browser.load(URLRequest(url: self.startURL))
            }
        }
    }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        let origin = message.frameInfo.securityOrigin
        guard message.frameInfo.isMainFrame, origin.protocol == "https",
              let url = message.frameInfo.request.url, origin.host == url.host,
              url.user == nil, url.port == nil || url.port == 443,
              let value = message.body as? String else { return }
        if message.name == "MemoLexAuthorization", isTokenDocument(url), value.count <= 65536 {
            acceptAuthorization(value, from: url, finished: false)
            return
        }
        guard message.name == "MemoLexIdentity", origin.host == "accounts.maimemo.com",
              url.path.hasPrefix("/interaction/"), value.count < 200 else { return }
        let normalized = value.trimmed.lowercased()
        if normalized.range(of: "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$", options: .regularExpression) != nil { identity = normalized }
        else if normalized.range(of: "^\\+?[0-9 ()-]+$", options: .regularExpression) != nil { let digits = normalized.filter(\.isNumber); if (7...18).contains(digits.count) { identity = digits.count == 13 && digits.hasPrefix("86") ? String(digits.dropFirst(2)) : digits } }
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
        if url.absoluteString == "about:blank" { decisionHandler(.allow); return }
        guard url.scheme == "https", url.user == nil, url.port == nil || url.port == 443, ["accounts.maimemo.com", "open.maimemo.com", "www.maimemo.com"].contains(url.host ?? "") else { status = "此链接不属于墨墨官方登录页面。"; decisionHandler(.cancel); return }
        if navigationAction.targetFrame?.isMainFrame == true, isTokenDocument(url) { showsLoginForm = false }
        decisionHandler(.allow)
    }
    func webView(_ webView: WKWebView, didCommit navigation: WKNavigation!) {
        guard let url = webView.url else { return }
        showsLoginForm = !isTokenDocument(url) && url.scheme == "https"
        status = showsLoginForm ? "请在墨墨官方页面完成登录。" : "正在完成登录…"
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard let url = webView.url, isTokenDocument(url) else { return }
        webView.evaluateJavaScript("document.querySelector('body > pre')?.textContent || document.documentElement.outerHTML") { [weak self] value, _ in
            Task { @MainActor in
                self?.acceptAuthorization(value as? String ?? "", from: url, finished: true)
            }
        }
    }
    private func isTokenDocument(_ url: URL) -> Bool {
        url.scheme == "https" && url.host == startURL.host && url.path == startURL.path
            && url.user == nil && (url.port == nil || url.port == 443)
    }
    private func acceptAuthorization(_ content: String, from url: URL, finished: Bool) {
        guard model.showLogin, browser.url.map(isTokenDocument) == true else { return }
        if completion != nil {
            if finished { finishedCapture = (content, url) }
            return
        }
        showsLoginForm = false
        busy = true
        status = "正在完成登录…"
        completion = Task { [weak self] in
            guard let self else { return }
            defer {
                completion = nil; busy = false
                if let (body, url) = finishedCapture {
                    finishedCapture = nil
                    if !Task.isCancelled { acceptAuthorization(body, from: url, finished: true) }
                }
            }
            do {
                let authorization = try await model.core.request("login.inspect", ["url": .string(url.absoluteString), "content": .string(content)], timeout: 8)
                guard !Task.isCancelled, model.showLogin else { return }
                if authorization == .null {
                    if finished || !browser.isLoading { status = "未能读取登录结果，请点击“重新打开”再试。" }
                    return
                }
                try await model.finishLogin(authorization, identity: identity, fromOfficialPage: true)
            } catch {
                if !Task.isCancelled { status = error.localizedDescription }
            }
        }
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { status = "登录页面加载失败，请重新打开。" }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { if (error as NSError).code != NSURLErrorCancelled { status = "暂时无法连接墨墨登录页，请检查网络后重试。" } }
    func stop() {
        finishedCapture = nil
        completion?.cancel()
        browser.stopLoading()
        let controller = browser.configuration.userContentController
        controller.removeScriptMessageHandler(forName: "MemoLexIdentity")
        controller.removeScriptMessageHandler(forName: "MemoLexAuthorization")
    }
}

struct BrowserHost: NSViewRepresentable {
    let browser: WKWebView
    var hidden = false
    func makeNSView(context: Context) -> WKWebView { browser.isHidden = hidden; return browser }
    func updateNSView(_ browser: WKWebView, context: Context) { browser.isHidden = hidden }
}

struct LoginView: View {
    @ObservedObject var model: AppModel
    @StateObject private var login: LoginBrowser
    init(model: AppModel) { self.model = model; _login = StateObject(wrappedValue: LoginBrowser(model: model)) }
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "登录墨墨官方账号") { model.showLogin = false }
            Text(model.loginTarget.map { "请使用 \($0.displayName) 登录墨墨" } ?? "墨墨官方登录 · https://accounts.maimemo.com")
                .font(.system(size: 12)).foregroundStyle(Palette.muted).frame(maxWidth: .infinity, alignment: .leading).padding(14)
            ZStack {
                BrowserHost(browser: login.browser, hidden: !login.showsLoginForm)
                if !login.showsLoginForm { Text(login.status.statusMessage).foregroundStyle(.secondary) }
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
            HStack { Spacer(); if login.busy { ProgressView().controlSize(.small) }; Button("重新打开") { login.retry() }.disabled(login.busy); Button("取消") { model.showLogin = false }.keyboardShortcut(.cancelAction) }.padding(15)
        }.frame(width: min(740, (NSScreen.main?.visibleFrame.width ?? 900) - 60), height: min(760, (NSScreen.main?.visibleFrame.height ?? 860) - 65)).background(Palette.canvas).onDisappear { login.stop() }
    }
}

@MainActor final class HaiciBrowser: NSObject, ObservableObject, WKNavigationDelegate, WKUIDelegate {
    let browser = WKWebView()
    @Published var url: URL
    @Published var canGoBack = false
    @Published var canGoForward = false
    @Published var status = "海词词典 · 在线内容"
    private var observations: [NSKeyValueObservation] = []
    init(word: String) {
        let encoded = word.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/?#%"))) ?? ""
        url = URL(string: "https://dict.cn/\(encoded)")!
        super.init(); browser.navigationDelegate = self; browser.uiDelegate = self
        observations = [browser.observe(\.url, options: .new) { [weak self] browser, _ in Task { @MainActor in if let url = browser.url { self?.url = url } } }, browser.observe(\.canGoBack, options: .new) { [weak self] browser, _ in Task { @MainActor in self?.canGoBack = browser.canGoBack } }, browser.observe(\.canGoForward, options: .new) { [weak self] browser, _ in Task { @MainActor in self?.canGoForward = browser.canGoForward } }]
        browser.load(URLRequest(url: url))
    }
    static func allowed(_ url: URL) -> Bool { url.scheme == "https" && url.user == nil && (url.port == nil || url.port == 443) && (url.host == "dict.cn" || url.host?.hasSuffix(".dict.cn") == true) }
    func navigate(word: String) { if let encoded = word.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/?#%"))), let next = URL(string: "https://dict.cn/\(encoded)") { browser.load(URLRequest(url: next)) } }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) { decisionHandler(navigationAction.request.url.map(Self.allowed) == true ? .allow : .cancel) }
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? { if let url = navigationAction.request.url, Self.allowed(url) { browser.load(navigationAction.request) }; return nil }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { status = "暂时无法打开海词词典，请刷新或使用默认浏览器。" }
}

struct HaiciView: View {
    @ObservedObject var browser: HaiciBrowser
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) { Button("←") { browser.browser.goBack() }.disabled(!browser.canGoBack); Button("→") { browser.browser.goForward() }.disabled(!browser.canGoForward); Button("刷新") { browser.browser.reload() }; Text(browser.url.absoluteString).lineLimit(1).truncationMode(.middle).foregroundStyle(Palette.muted).frame(maxWidth: .infinity, alignment: .leading); Button("用默认浏览器打开") { if HaiciBrowser.allowed(browser.url) { NSWorkspace.shared.open(browser.url) } } }.buttonStyle(MemoButtonStyle(compact: true)).padding(12)
            BrowserHost(browser: browser.browser)
            Text(browser.status.statusMessage).font(.system(size: 11)).foregroundStyle(Palette.muted).frame(maxWidth: .infinity, alignment: .leading).padding(12)
        }.background(Palette.paper)
    }
}
