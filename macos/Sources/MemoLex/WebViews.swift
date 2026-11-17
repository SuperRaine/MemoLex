import AppKit
import SwiftUI
import WebKit

@MainActor final class LoginBrowser: NSObject, ObservableObject, WKNavigationDelegate, WKScriptMessageHandler {
    let browser: WKWebView
    @Published var status = "正在打开墨墨官方登录页…"
    @Published var busy = false
    private unowned let model: AppModel
    private var identity: String
    private let target: SavedAccount?
    private var completing = false
    private let startURL = URL(string: "https://open.maimemo.com/open/api/v1/tokens/openapi")!
    init(model: AppModel) {
        self.model = model; target = model.loginTarget; identity = ""
        let configuration = WKWebViewConfiguration(); configuration.websiteDataStore = .nonPersistent()
        let controller = WKUserContentController()
        controller.addUserScript(WKUserScript(source: """
        (() => {
          if (location.protocol !== 'https:' || location.hostname !== 'accounts.maimemo.com' || !location.pathname.startsWith('/interaction/')) return;
          const report = () => {
            const fields = [...document.querySelectorAll('input')];
            const field = fields.find(x => {
              if (!['text', 'tel', 'email', 'number'].includes(x.type) || !/phone|mobile|email|username|login|identifier|account|tel/i.test((x.name || '') + ' ' + (x.id || '') + ' ' + x.type)) return false;
              const value = x.value.trim(); const digits = value.replace(/\D/g, '');
              return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value) || (/^\+?[\d ()-]+$/.test(value) && digits.length >= 7 && digits.length <= 18);
            });
            if (field && field.value.length < 200) window.webkit.messageHandlers.MemoLexIdentity.postMessage(field.value);
          };
          document.addEventListener('input', report, true);
          document.addEventListener('submit', report, true);
        })();
        """, injectionTime: .atDocumentStart, forMainFrameOnly: false))
        configuration.userContentController = controller
        browser = WKWebView(frame: .zero, configuration: configuration)
        super.init(); controller.add(self, name: "MemoLexIdentity"); browser.navigationDelegate = self
        browser.load(URLRequest(url: startURL))
    }
    func retry() { completing = false; browser.load(URLRequest(url: startURL)); status = "请使用墨墨官方密码或验证码登录，完成后会自动返回。" }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        let origin = message.frameInfo.securityOrigin
        guard origin.protocol == "https", origin.host == "accounts.maimemo.com", message.frameInfo.request.url?.path.hasPrefix("/interaction/") == true, let value = message.body as? String, value.count < 200 else { return }
        let normalized = value.trimmed.lowercased()
        if normalized.range(of: "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$", options: .regularExpression) != nil { identity = normalized }
        else if normalized.range(of: "^\\+?[0-9 ()-]+$", options: .regularExpression) != nil { let digits = normalized.filter(\.isNumber); if (7...18).contains(digits.count) { identity = digits.count == 13 && digits.hasPrefix("86") ? String(digits.dropFirst(2)) : digits } }
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
        if url.absoluteString == "about:blank" { decisionHandler(.allow); return }
        guard url.scheme == "https", url.user == nil, url.port == nil || url.port == 443, ["accounts.maimemo.com", "open.maimemo.com", "www.maimemo.com"].contains(url.host ?? "") else { status = "此链接不属于墨墨官方登录页面。"; decisionHandler(.cancel); return }
        decisionHandler(.allow)
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard let documentURL = webView.url, documentURL.scheme == "https", documentURL.host == "open.maimemo.com", documentURL.path == startURL.path, documentURL.user == nil, documentURL.port == nil || documentURL.port == 443, !completing else { status = "请在墨墨官方页面完成登录。"; return }
        completing = true; busy = true; status = "正在确认墨墨登录信息…"
        webView.evaluateJavaScript("document.documentElement.outerHTML") { [weak self] value, _ in
            Task { @MainActor in
                guard let self else { return }
                defer { self.busy = false }
                do {
                    guard let html = value as? String else { throw CoreFailure(code: "login", message: "未能读取登录结果，请重新打开登录页。") }
                    let authorization = try await self.model.core.request("login.inspect", ["url": .string(documentURL.absoluteString), "content": .string(html)])
                    guard authorization != .null else { throw CoreFailure(code: "login", message: "未能确认登录信息，请点击“重新打开”再试。") }
                    try await self.model.finishLogin(authorization, identity: self.identity, target: self.target)
                } catch { self.status = error.localizedDescription; self.completing = false }
            }
        }
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { status = "登录页面加载失败，请重新打开。" }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { if (error as NSError).code != NSURLErrorCancelled { status = "暂时无法连接墨墨登录页，请检查网络后重试。" } }
    func stop() { browser.stopLoading(); browser.configuration.userContentController.removeScriptMessageHandler(forName: "MemoLexIdentity") }
}

struct BrowserHost: NSViewRepresentable {
    let browser: WKWebView
    func makeNSView(context: Context) -> WKWebView { browser }
    func updateNSView(_ browser: WKWebView, context: Context) {}
}

struct LoginView: View {
    @ObservedObject var model: AppModel
    @StateObject private var login: LoginBrowser
    init(model: AppModel) { self.model = model; _login = StateObject(wrappedValue: LoginBrowser(model: model)) }
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "登录墨墨官方账号") { model.showLogin = false }
            Text("墨墨官方登录 · https://accounts.maimemo.com").font(.system(size: 12)).foregroundStyle(Palette.muted).frame(maxWidth: .infinity, alignment: .leading).padding(14)
            BrowserHost(browser: login.browser).frame(maxWidth: .infinity, maxHeight: .infinity)
            HStack { Text(login.status).font(.system(size: 12)).foregroundStyle(Palette.muted); Spacer(); if login.busy { ProgressView().controlSize(.small) }; Button("重新打开") { login.retry() }.buttonStyle(MemoButtonStyle()); Button("取消") { model.showLogin = false }.buttonStyle(MemoButtonStyle()) }.padding(15)
        }.frame(width: min(740, (NSScreen.main?.visibleFrame.width ?? 900) - 60), height: min(760, (NSScreen.main?.visibleFrame.height ?? 860) - 65)).background(.white).onDisappear { login.stop() }
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
    var close: () -> Void
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "海词词典", close: close)
            HStack(spacing: 8) { Button("←") { browser.browser.goBack() }.disabled(!browser.canGoBack); Button("→") { browser.browser.goForward() }.disabled(!browser.canGoForward); Button("刷新") { browser.browser.reload() }; Text(browser.url.absoluteString).lineLimit(1).truncationMode(.middle).foregroundStyle(Palette.muted).frame(maxWidth: .infinity, alignment: .leading); Button("用默认浏览器打开") { if HaiciBrowser.allowed(browser.url) { NSWorkspace.shared.open(browser.url) } } }.buttonStyle(MemoButtonStyle(compact: true)).padding(12)
            BrowserHost(browser: browser.browser)
            Text(browser.status).font(.system(size: 11)).foregroundStyle(Palette.muted).frame(maxWidth: .infinity, alignment: .leading).padding(12)
        }.background(.white)
    }
}
