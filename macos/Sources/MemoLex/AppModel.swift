import AppKit
import AVFoundation
import Combine
import Foundation
import SwiftUI

enum AppSheet: String, Identifiable { case settings, history, stubborn, mnemonic, notepad; var id: String { rawValue } }
struct Notice: Identifiable {
    let id = UUID()
    var title: String
    var message: String
    var confirm: String? = nil
    var destructive = false
    var action: (() -> Void)? = nil
}

@MainActor final class AppModel: ObservableObject {
    let preferences = Preferences()
    let core = CoreBridge()
    @Published var ready = false
    @Published var status = "正在准备词典…"
    @Published var wordCount = 0
    @Published var preferredDictionary = J.null
    @Published var dictionaryLoading = false
    @Published var search = ""
    @Published var suggestions: [String] = []
    @Published var entry = J.null
    @Published var word = ""
    @Published var notes: [J] = []
    @Published var relations = J.null
    @Published var expandedRelations: Set<String> = []
    @Published var history: [SearchRecord] = []
    @Published var page: ViewLocation.Page = .word
    @Published var chineseResults: [J] = []
    @Published var chineseLimit = 160
    @Published var chineseTotal = 0
    @Published var examples: [J] = []
    @Published var exampleTotal = 0
    @Published var exampleOffset = 0
    @Published var examplePaper = "ALL"
    @Published var examplesLoading = false
    @Published var examplesError = ""
    @Published var translationInput = ""
    @Published var translationOutput = ""
    @Published var translationSource = ""
    @Published var translationDirection = "en|zh-CN"
    @Published var translationProvider = "DeepL"
    @Published var translating = false
    @Published var accounts: [SavedAccount] = []
    @Published var activeAccountID = ""
    @Published var loggedIn = false
    @Published var accountStatus = "未登录"
    @Published var showLogin = false
    @Published var loginTarget: SavedAccount?
    @Published var notepads: [J] = []
    @Published var selectedPad = J.null
    @Published var notebookRows: [J] = []
    @Published var notebookLoading = false
    @Published var todayStatus = ""
    @Published var membership = false
    @Published var writing = false
    @Published var stubbornWords: [J] = []
    @Published var stubbornOrder: [J] = []
    @Published var stubbornIndex = 0
    @Published var stubbornTotal = 0
    @Published var stubbornLoading = false
    @Published var stubbornHint = ""
    @Published var sheet: AppSheet?
    @Published var settingsPage = "常规"
    @Published var notice: Notice? { didSet { if notice != nil { presentNotice?() } } }
    @Published var tourIndex: Int? = nil
    @Published var canGoBack = false
    @Published var canGoForward = false
    var showWindow: (() -> Void)?
    var applySystemPreferences: (() -> Void)?
    var captureScreen: (() -> Void)?
    var openHaici: ((String) -> Void)?
    var focusSearch: (() -> Void)?
    var presentNotice: (() -> Void)?
    var removingApplication = false
    var updates: MacUpdates!
    private var navigation: [ViewLocation] = []
    private var navigationIndex = -1
    private var wordVersion = 0
    private var accountVersion = 0
    private var examplesVersion = 0
    private var translationVersion = 0
    private var suggestionTask: Task<Void, Never>?
    private var lookupTask: Task<Void, Never>?
    private var accountTask: Task<Void, Never>?
    private var preferencesObserver: AnyCancellable?
    private var preferencesSave: Task<Void, Never>?
    private var lastStubbornFetch: Date?
    private var nextStubbornRetry: Date?
    private var nextAccountCheck: Date?
    private let speaker = AVSpeechSynthesizer()

    init() {
        accounts = load("accounts.json", as: [SavedAccount].self) ?? []
        history = load("history.json", as: [SearchRecord].self) ?? []
        activeAccountID = UserDefaults.standard.string(forKey: "activeAccount") ?? ""
        core.onUnauthorized = { [weak self] in self?.expireAccount() }
        preferencesObserver = preferences.objectWillChange.sink { [weak self] _ in self?.schedulePreferencesSave() }
        updates = MacUpdates(preferences: preferences)
    }
    var activeAccount: SavedAccount? { accounts.first { $0.id == activeAccountID } }
    var titleAccount: String { activeAccount?.displayName ?? accountStatus }
    var stubbornCurrent: String { stubbornOrder.indices.contains(stubbornIndex) ? stubbornOrder[stubbornIndex]["spelling"].text : "" }
    var selectedNotepadTitle: String { selectedPad["title"].text }
    func initialize() async {
        Task { await restoreAccount() }
        do {
            let metadata = try await core.request("initialize")
            wordCount = metadata["wordCount"].integer; translationProvider = metadata["translationProvider"].text
            preferredDictionary = metadata["preferredDictionary"]
            ready = true; status = metadata["warning"].text.isEmpty ? "词典与本地助记已就绪。" : metadata["warning"].text
            updates.start()
            if !preferences.onboardingCompleted { startTour() }
        } catch { fail(error) }
    }
    private func restoreAccount() async {
        guard let account = activeAccount else { return }
        if account.needsLogin { accountStatus = "请重新登录"; return }
        await activate(account.id)
    }
    func configureDictionary(path: String) async {
        guard ready, !dictionaryLoading else { return }
        dictionaryLoading = true
        defer { dictionaryLoading = false }
        do {
            preferredDictionary = try await core.request("dictionary.configure", ["path": .string(path)])
            suggest()
            if page != .translation, !search.trimmed.isEmpty { lookup(search, navigate: false, foreground: false) }
        } catch { fail(error) }
    }
    func suggest() {
        suggestionTask?.cancel()
        let query = search.trimmed
        guard ready, !query.isEmpty else { suggestions = []; return }
        suggestionTask = Task {
            try? await Task.sleep(nanoseconds: 120_000_000)
            guard !Task.isCancelled else { return }
            if let result = try? await core.request("dictionary.suggest", ["word": .string(query)]), query == search.trimmed { suggestions = result.list.map(\.text) }
        }
    }
    func lookup(_ raw: String, navigate: Bool = true, foreground: Bool = true) {
        let query = raw.trimmed
        guard ready, !query.isEmpty, query.count <= 100 else { return }
        lookupTask?.cancel(); wordVersion += 1
        let version = wordVersion; let account = accountVersion
        search = query
        if foreground { showWindow?() }
        lookupTask = Task {
            do {
                let chinese = query.range(of: "[\\p{Han}]", options: .regularExpression) != nil
                if chinese {
                    page = .chinese; chineseLimit = 160
                    let result = try await core.request("dictionary.reverse", ["query": .string(query), "limit": .number(Double(chineseLimit))])
                    guard version == wordVersion else { return }
                    chineseResults = result["items"].list; chineseTotal = result["total"].integer
                    status = "找到 \(chineseTotal) 个相关结果。"
                } else {
                    page = .word; word = query; entry = .null; notes = []; relations = .null; examples = []; todayStatus = ""; membership = false
                    let result = try await core.request("dictionary.lookup", ["word": .string(query)])
                    guard version == wordVersion else { return }
                    word = result["word"].text
                    entry = result["entry"]
                    if account == accountVersion { notes = result["notes"].list }
                    suggestions = result["suggestions"].list.map(\.text)
                    recordHistory(word); status = entry == .null ? "本地词库未收录这个单词。" : "查词完成。"
                    Task { if let value = try? await core.request("dictionary.related", ["word": .string(word)]), version == wordVersion { relations = value } }
                    Task { await loadExamples(reset: true) }
                    if loggedIn {
                        let currentAccount = accountVersion
                        Task { await loadCloudWord(word, version: version, account: currentAccount) }
                    }
                }
                if navigate { appendLocation(.init(page: page, query: query)) }
            } catch { if version == wordVersion { fail(error) } }
        }
    }
    func moreChinese() async {
        chineseLimit += 160
        if let result = try? await core.request("dictionary.reverse", ["query": .string(search), "limit": .number(Double(chineseLimit))]) { chineseResults = result["items"].list }
    }
    private func loadCloudWord(_ query: String, version: Int, account: Int) async {
        async let cloudNotes = try? core.request("notes.cloud", ["word": .string(query)])
        async let study = try? core.request("study.status", ["word": .string(query)])
        let result = await (cloudNotes, study)
        guard version == wordVersion, account == accountVersion else { return }
        if let value = result.0 { notes = value.list }
        todayStatus = result.1?["status"].text ?? ""
        await refreshMembership()
    }
    func loadExamples(reset: Bool) async {
        if reset { examplesVersion += 1; exampleOffset = 0; examples = [] }
        let version = examplesVersion; let query = word; let paper = examplePaper
        guard !query.isEmpty else { return }
        examplesLoading = true; examplesError = ""
        do {
            let result = try await core.request("examples", ["word": .string(query), "paperType": .string(paper), "offset": .number(Double(exampleOffset))])
            guard version == examplesVersion, query == word, paper == examplePaper else { return }
            examples += result["items"].list; exampleTotal = result["total"].integer; exampleOffset += result["receivedCount"].integer
        } catch { if version == examplesVersion { examplesError = error.localizedDescription } }
        if version == examplesVersion { examplesLoading = false }
    }
    func speak(_ value: String? = nil) {
        let text = value ?? word; guard !text.isEmpty else { return }
        speaker.stopSpeaking(at: .immediate)
        let utterance = AVSpeechUtterance(string: text); utterance.voice = AVSpeechSynthesisVoice(language: "en-US"); utterance.rate = 0.42; speaker.speak(utterance)
    }
    func enterTranslation() { page = .translation; appendLocation(.init(page: .translation, query: "段落翻译", input: translationInput, translated: translationOutput, direction: translationDirection)) }
    func translate() async {
        guard !translationInput.trimmed.isEmpty else { return }
        translationVersion += 1; let version = translationVersion
        translating = true; translationSource = translationInput
        do {
            let result = try await core.request("translate", ["text": .string(translationInput), "direction": .string(translationDirection)])
            guard version == translationVersion else { return }
            translationOutput = result["text"].text; translationProvider = result["provider"].text
            if navigation.indices.contains(navigationIndex), page == .translation { navigation[navigationIndex] = .init(page: .translation, query: "段落翻译", input: translationInput, translated: translationOutput, direction: translationDirection) }
        } catch { if version == translationVersion { fail(error) } }
        if version == translationVersion { translating = false }
    }
    func classify(_ text: String, foreground: Bool = true) async {
        do {
            let result = try await core.request("text.classify", ["text": .string(text)])
            if foreground { showWindow?() }
            switch result["kind"].text {
            case "Word", "Chinese": lookup(result["text"].text)
            case "Paragraph": translationInput = result["text"].text; translationDirection = result["direction"].text; enterTranslation(); await translate()
            default: status = "未识别到可查询的文字。"
            }
        } catch { fail(error) }
    }
    private func appendLocation(_ location: ViewLocation) {
        if navigation.indices.contains(navigationIndex), navigation[navigationIndex] == location { return }
        if navigationIndex + 1 < navigation.count { navigation.removeSubrange((navigationIndex + 1)..<navigation.count) }
        navigation.append(location); navigationIndex = navigation.count - 1; navigationFlags()
    }
    func move(_ direction: Int) {
        let target = navigationIndex + direction; guard navigation.indices.contains(target) else { return }
        navigationIndex = target; let location = navigation[target]; page = location.page
        if page == .translation { translationInput = location.input; translationSource = location.input; translationOutput = location.translated; translationDirection = location.direction }
        else { lookup(location.query, navigate: false) }
        navigationFlags()
    }
    private func navigationFlags() { canGoBack = navigationIndex > 0; canGoForward = navigationIndex >= 0 && navigationIndex < navigation.count - 1 }
    private func recordHistory(_ word: String) {
        let prior = history.first { $0.word.lowercased() == word.lowercased() }; history.removeAll { $0.word.lowercased() == word.lowercased() }
        history.insert(.init(word: word, date: Date(), count: (prior?.count ?? 0) + 1), at: 0); if history.count > 500 { history.removeLast(history.count - 500) }; save(history, "history.json")
    }
    func removeHistory(_ ids: Set<String>) { history.removeAll { ids.contains($0.id) }; save(history, "history.json") }
    func clearHistory() { history = []; save(history, "history.json") }

    func activate(_ id: String, token: String? = nil, verified: Bool = false) async {
        guard let account = accounts.first(where: { $0.id == id }) else { return }
        accountVersion += 1
        let generation = accountVersion
        accountTask?.cancel()
        loggedIn = false
        clearCloudState()
        activeAccountID = id
        UserDefaults.standard.set(id, forKey: "activeAccount")
        accountStatus = "正在连接"
        do {
            if account.needsLogin || (account.expiresAt.map { $0 <= Date() } ?? false) {
                if !account.needsLogin { expireAccount() }
                else { accountStatus = "请重新登录"; try await core.setAccount(token: nil) }
                return
            }
            let credential = try token ?? CredentialVault.read(id)
            guard let credential else { expireAccount(); return }
            try await core.setAccount(token: credential)
            guard generation == accountVersion else { return }
            loggedIn = true
            let query = word
            if !query.isEmpty {
                Task {
                    if let result = try? await core.request("notes.local", ["word": .string(query)]),
                       generation == accountVersion, query == word { notes = result.list }
                }
            }
            checkAccountConnection(generation, verified: verified)
        } catch {
            guard generation == accountVersion else { return }
            accountStatus = "连接未完成"
            status = error.localizedDescription
            nextAccountCheck = Date().addingTimeInterval(45)
        }
    }
    private func checkAccountConnection(_ generation: Int, verified: Bool = false) {
        accountTask = Task { [weak self] in
            guard let self else { return }
            do {
                if !verified { _ = try await core.request("account.validate", timeout: 55) }
                guard generation == accountVersion, !Task.isCancelled else { return }
                accountStatus = "已登录"
                nextAccountCheck = nil
                async let stubborn: Void = refreshStubborn(force: true)
                async let pads: Void = refreshNotepads()
                if !word.isEmpty { await loadCloudWord(word, version: wordVersion, account: generation) }
                _ = await (stubborn, pads)
            } catch {
                guard generation == accountVersion, !Task.isCancelled else { return }
                accountStatus = "暂时离线"
                status = error.localizedDescription
                nextAccountCheck = Date().addingTimeInterval(45)
            }
        }
    }
    func finishLogin(_ authorization: J, identity: String, fromOfficialPage: Bool = false) async throws {
        let token = authorization["token"].text
        guard !token.isEmpty else { throw CoreFailure(code: "login", message: "未能获取登录信息，请重新登录。") }
        if !fromOfficialPage { _ = try await core.request("login.validate", ["token": .string(token)]) }
        let identity = identity.trimmed.lowercased()
        let sameIdentity = identity.isEmpty ? nil : accounts.first { $0.identity == identity }
        let sameCredential = accounts.first { (try? CredentialVault.read($0.id)) == token }
        if let sameCredential, !sameCredential.identity.isEmpty, !identity.isEmpty, sameCredential.identity != identity {
            throw CoreFailure(code: "login", message: "本次授权属于另一个已保存账号，请重新登录对应账号。")
        }
        let existing = sameIdentity ?? sameCredential
        let id = existing?.id ?? UUID().uuidString
        try CredentialVault.save(token, account: id)
        let resolvedIdentity = identity.isEmpty ? (existing?.identity ?? "") : identity
        let isPlaceholder = existing?.name.range(of: "^墨墨账号 [0-9]+$", options: .regularExpression) != nil
        let name: String
        if let existing, !isPlaceholder || resolvedIdentity.isEmpty {
            name = existing.displayName
        } else {
            name = resolvedIdentity.isEmpty ? "墨墨账号 \(accounts.count + 1)" : SavedAccount.displayIdentity(resolvedIdentity)
        }
        let account = SavedAccount(id: id, name: name, identity: resolvedIdentity, expiresAt: parseISODate(authorization["expiresAt"].text))
        if let index = accounts.firstIndex(where: { $0.id == id }) { accounts[index] = account } else { accounts.append(account) }
        save(accounts, "accounts.json")
        showLogin = false
        Task { await activate(id, token: token, verified: !fromOfficialPage) }
    }
    func logout() async {
        accountVersion += 1
        let generation = accountVersion
        accountTask?.cancel()
        loggedIn = false
        activeAccountID = ""
        accountStatus = "未登录"
        clearCloudState()
        UserDefaults.standard.removeObject(forKey: "activeAccount")
        await clearWorkerAccount(generation)
    }
    func expireAccount() {
        guard let index = accounts.firstIndex(where: { $0.id == activeAccountID }), !accounts[index].needsLogin else { return }
        let id = accounts[index].id
        accounts[index].needsLogin = true
        CredentialVault.remove(id)
        save(accounts, "accounts.json")
        accountVersion += 1
        let generation = accountVersion
        accountTask?.cancel()
        loggedIn = false
        accountStatus = "登录已失效"
        clearCloudState()
        Task { await clearWorkerAccount(generation) }
        notice = .init(title: "墨墨登录已失效", message: "请重新登录或导入有效的墨墨 API 授权。登录后，云词本和顽固单词会自动刷新。", confirm: "账号设置", action: { [weak self] in self?.openAccountSettings() })
    }
    private func clearWorkerAccount(_ generation: Int) async {
        guard generation == accountVersion else { return }
        try? await core.setAccount(token: nil)
        let query = word
        guard !query.isEmpty, let value = try? await core.request("notes.local", ["word": .string(query)]),
              generation == accountVersion, query == word else { return }
        notes = value.list
    }
    func checkExpiry() {
        guard let account = activeAccount, !account.needsLogin else { return }
        if let expiry = account.expiresAt, expiry <= Date() { expireAccount(); return }
        guard let retry = nextAccountCheck, retry <= Date() else { return }
        nextAccountCheck = nil
        if loggedIn { checkAccountConnection(accountVersion) }
        else { Task { await activate(account.id) } }
    }
    func renameAccount(_ id: String, _ name: String) {
        guard let index = accounts.firstIndex(where: { $0.id == id }), !name.trimmed.isEmpty else { return }
        accounts[index].name = String(name.trimmed.prefix(80)); save(accounts, "accounts.json")
    }
    func removeAccount(_ id: String) async { if activeAccountID == id { await logout() }; CredentialVault.remove(id); accounts.removeAll { $0.id == id }; save(accounts, "accounts.json") }
    private func clearCloudState() {
        notes = []; notepads = []; selectedPad = .null; notebookRows = []; notebookLoading = false
        membership = false; todayStatus = ""; writing = false
        stubbornWords = []; stubbornOrder = []; stubbornIndex = 0; stubbornTotal = 0; stubbornLoading = false
        stubbornHint = ""; lastStubbornFetch = nil; nextStubbornRetry = nil; nextAccountCheck = nil
    }
    func refreshNotepads() async {
        guard loggedIn else { return }; let generation = accountVersion
        do {
            let result = try await core.request("notepads.list"); guard generation == accountVersion else { return }; notepads = result.list
            let id = notepads.contains { $0["id"].text == preferences.selectedNotepad } ? preferences.selectedNotepad : notepads.first?["id"].text ?? ""
            preferences.selectedNotepad = id; preferences.save(); await selectNotepad(id)
        } catch { if generation == accountVersion { status = error.localizedDescription } }
    }
    func selectNotepad(_ id: String) async {
        guard loggedIn, !id.isEmpty else { selectedPad = .null; return }; let generation = accountVersion
        preferences.selectedNotepad = id; preferences.save()
        do {
            let value = try await core.request("notepads.get", ["id": .string(id)])
            guard generation == accountVersion, preferences.selectedNotepad == id else { return }; selectedPad = value
            await refreshMembership()
        } catch { if generation == accountVersion { status = error.localizedDescription } }
    }
    func refreshMembership() async { membership = selectedPad["content"].text.components(separatedBy: .newlines).contains { $0.trimmed.caseInsensitiveCompare(word) == .orderedSame && !$0.trimmed.hasPrefix("#") } }
    func toggleMembership() async {
        guard loggedIn else { openAccountSettings(); return }; guard !word.isEmpty, !writing else { return }
        if selectedPad == .null { createNotebook(); return }
        if membership, selectedPad["words"].list.count <= 1 { notice = .init(title: "删除整个云词本？", message: "这是词本中的最后一个单词。删除后，整个云词本也会被删除。", confirm: "删除词本", destructive: true, action: { [weak self] in Task { await self?.deleteNotebook() } }); return }
        writing = true; let generation = accountVersion; let id = selectedPad["id"].text
        do {
            let result = try await core.request(membership ? "notepads.remove" : "notepads.add", ["id": .string(id), "word": .string(word)])
            if generation == accountVersion, preferences.selectedNotepad == id { selectedPad = result["notepad"]; await refreshMembership(); status = membership ? "已加入云词本。" : "已从云词本移除。" }
        } catch { if generation == accountVersion { fail(error) } }; if generation == accountVersion { writing = false }
    }
    func addToday() {
        guard loggedIn else { openAccountSettings(); return }
        notice = .init(title: "添加到今日新学", message: "请在墨墨背单词 → 我的 → 学习数据同步 → 学习数据自动同步中开启同步，便于及时更新学习状态。", confirm: "添加", action: { [weak self] in Task { await self?.performAddToday() } })
    }
    private func performAddToday() async {
        guard !writing, !word.isEmpty else { return }; writing = true; let generation = accountVersion; let query = word
        do { let result = try await core.request("study.add", ["word": .string(query)]); if generation == accountVersion, query == word { todayStatus = result["status"].text; switch todayStatus { case "AlreadyLearned": status = "这个单词已有学习记录。"; case "Added", "AlreadyToday", "Planned": status = "已安排今日新学。"; case "NotAdded": status = "尚未加入今日新学，请稍后重试。"; default: status = "已提交，学习状态还需等待手机端同步。" } } }
        catch { if generation == accountVersion { fail(error) } }; if generation == accountVersion { writing = false }
    }
    func createNotebook() {
        guard loggedIn else { openAccountSettings(); return }
        let alert = NSAlert(); alert.messageText = "新建云词本"; alert.addButton(withTitle: "创建"); alert.addButton(withTitle: "取消")
        let input = NSTextField(string: "我的生词本"); input.frame = NSRect(x: 0, y: 0, width: 320, height: 26); alert.accessoryView = input
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        let generation = accountVersion
        Task { do { let result = try await core.request("notepads.create", ["title": .string(input.stringValue), "word": .string(word)]); guard generation == accountVersion else { return }; preferences.selectedNotepad = result["id"].text; preferences.save(); await refreshNotepads() } catch { if generation == accountVersion { fail(error) } } }
    }
    func loadNotebookRows() async {
        notebookLoading = true
        do { notebookRows = try await core.request("dictionary.batch", ["content": selectedPad["content"]]).list }
        catch { fail(error) }; notebookLoading = false
    }
    func saveNotebook(original: J, title: String, brief: String, content: String) async throws {
        let generation = accountVersion
        let result = try await core.request("notepads.update", ["original": original, "title": .string(title), "brief": .string(brief), "content": .string(content)])
        guard generation == accountVersion else { return }; selectedPad = result; await loadNotebookRows(); await refreshMembership(); await refreshNotepads()
    }
    func deleteNotebook() async {
        guard selectedPad != .null else { return }
        let generation = accountVersion
        do { _ = try await core.request("notepads.delete", ["original": selectedPad]); guard generation == accountVersion else { return }; selectedPad = .null; sheet = nil; await refreshNotepads(); status = "云词本已删除。" }
        catch { if generation == accountVersion { fail(error) } }
    }
    func refreshStubborn(force: Bool = false) async {
        guard loggedIn, !stubbornLoading else { return }
        if !force, let retry = nextStubbornRetry, retry > Date() { return }
        if !force, let fetched = lastStubbornFetch, Date().timeIntervalSince(fetched) < 900 { return }
        stubbornLoading = true; let generation = accountVersion
        do {
            let result = try await core.request("stubborn")
            guard generation == accountVersion else { return }
            let previous = stubbornOrder.map { $0["spelling"].text }; let current = stubbornCurrent
            stubbornWords = result["words"].list; stubbornTotal = result["total"].integer; lastStubbornFetch = Date()
            nextStubbornRetry = nil
            stubbornHint = result["complete"].flag ? "" : "已获取 \(stubbornWords.count)/\(stubbornTotal)，部分记录受接口上限影响。"
            if preferences.stubbornSort == "random", Set(previous) == Set(stubbornWords.map { $0["spelling"].text }) {
                let updated = Dictionary(stubbornWords.map { ($0["spelling"].text, $0) }, uniquingKeysWith: { first, _ in first })
                stubbornOrder = previous.compactMap { updated[$0] }; stubbornIndex = stubbornOrder.firstIndex { $0["spelling"].text == current } ?? 0
            } else { reorderStubborn() }
        } catch {
            if generation == accountVersion, !Task.isCancelled {
                lastStubbornFetch = nil
                nextStubbornRetry = Date().addingTimeInterval(65)
                stubbornHint = error.localizedDescription
            }
        }
        if generation == accountVersion { stubbornLoading = false }
    }
    func reorderStubborn() {
        stubbornIndex = 0
        switch preferences.stubbornSort {
        case "alphabetical": stubbornOrder = stubbornWords.sorted { preferences.alphabetDescending ? $0["spelling"].text.localizedCaseInsensitiveCompare($1["spelling"].text) == .orderedDescending : $0["spelling"].text.localizedCaseInsensitiveCompare($1["spelling"].text) == .orderedAscending }
        case "random": stubbornOrder = stubbornWords.shuffled()
        default:
            let scheduled = stubbornWords.map { (word: $0, date: parseISODate($0["nextStudyDate"].text) ?? .distantFuture) }
            stubbornOrder = scheduled.sorted { left, right in
                if left.date != right.date { return left.date < right.date }
                return left.word["spelling"].text.localizedCaseInsensitiveCompare(right.word["spelling"].text) == .orderedAscending
            }.map(\.word)
        }
    }
    func nextStubborn() {
        guard stubbornOrder.count > 1 else { return }
        let previous = stubbornCurrent; stubbornIndex += 1
        if stubbornIndex >= stubbornOrder.count {
            stubbornIndex = 0
            if preferences.stubbornSort == "random" { stubbornOrder.shuffle(); if stubbornCurrent == previous { stubbornOrder.swapAt(0, Int.random(in: 1..<stubbornOrder.count)) } }
        }
    }
    func saveMnemonic(id: String, type: String, text: String, cloud: Bool, cloudID: String?) async throws {
        let query = word; let generation = wordVersion
        let result: J
        if cloud { result = try await core.request("notes.publish", ["word": .string(query), "id": .string(cloudID ?? ""), "noteType": .string(type), "text": .string(text)]) }
        else { result = try await core.request("notes.save", ["word": .string(query), "note": .object(["id": .string(id), "noteType": .string(type), "text": .string(text), "origin": .string("local")])]) }
        if generation == wordVersion { notes = result.list }; status = cloud ? "助记已同步到墨墨。" : "助记已保存到本地。"
    }
    func openAccountSettings() { settingsPage = "墨墨账号"; sheet = .settings }
    private func schedulePreferencesSave() {
        preferencesSave?.cancel()
        preferencesSave = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 220_000_000)
            guard !Task.isCancelled else { return }
            self?.preferences.save()
            self?.applySystemPreferences?()
        }
    }
    func fail(_ error: Error) { if let coreError = error as? CoreFailure, coreError.code == "cancelled" { return }; status = error.localizedDescription; notice = .init(title: "操作未完成", message: error.localizedDescription) }
    private func load<T: Decodable>(_ name: String, as type: T.Type) -> T? { guard let data = try? Data(contentsOf: AppPaths.profile.appendingPathComponent(name)) else { return nil }; return try? JSONDecoder().decode(type, from: data) }
    private func save<T: Encodable>(_ value: T, _ name: String) { do { try FileManager.default.createDirectory(at: AppPaths.profile, withIntermediateDirectories: true); try JSONEncoder().encode(value).write(to: AppPaths.profile.appendingPathComponent(name), options: .atomic) } catch { status = "本地资料暂时无法保存。" } }
}
