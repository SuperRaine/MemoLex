import AppKit
import AVFoundation
import Combine
import Foundation
import SwiftUI

enum AppSheet: String, Identifiable { case settings, history, stubborn, mnemonic, notepad, todayPlan, dashboard, contextReview, contextEditor, morphemePractice; var id: String { rawValue } }
struct PendingWordContext: Equatable {
    let word: String
    let matchedForm: String
    let sentence: String
    let translation: String
    let source: String
}
struct Notice: Identifiable {
    let id = UUID()
    var title: String
    var message: String
    var confirm: String? = nil
    var destructive = false
    var action: (() -> Void)? = nil
}

enum EnglishAccent: String, CaseIterable {
    case british = "en-GB"
    case american = "en-US"

    var label: String { self == .british ? "英音" : "美音" }
}

@MainActor final class AppModel: ObservableObject {
    let preferences = Preferences()
    let core = CoreBridge()
    @Published var ready = false
    @Published var status = "正在准备词典…"
    @Published var wordCount = 0
    @Published var search = ""
    @Published var suggestions: [String] = []
    @Published var spellingSuggestions: [String] = []
    @Published var extractedWords: [J] = []
    @Published var contextSentences: [J] = []
    @Published var activeSpellingMistakes: [J] = []
    @Published var pendingContext: PendingWordContext? = nil
    @Published var contextReviewFocusWord: String? = nil
    @Published var editingContextSentence: J? = nil
    @Published var morphemePracticeGroup: J? = nil
    @Published var recallMode = false
    @Published var recallProgress: Double = 0
    @Published var recallReady = false
    var lastTranslationSourceLabel = "段落翻译"
    private var recallTimerTask: Task<Void, Never>?
    @Published var entry = J.null
    @Published private(set) var onlineDefinition = J.null
    @Published private(set) var definitionLoading = false
    @Published var word = ""
    @Published var notes: [J] = []
    @Published var relations = J.null
    @Published var expandedRelations: Set<String> = []
    @Published var history: [SearchRecord] = []
    @Published var page: ViewLocation.Page = .word
    @Published var chineseResults: [J] = []
    @Published private(set) var chineseQuery = ""
    @Published private(set) var chineseHint = ""
    @Published var chineseLimit = 160
    @Published var chineseTotal = 0
    @Published var examples: [J] = []
    @Published var exampleTotal = 0
    @Published var exampleOffset = 0
    @Published var examplePaper = "ALL"
    @Published var examplesLoading = false
    @Published var examplesError = ""
    @Published private(set) var loadingExampleIndex: Int?
    @Published var translationInput = ""
    @Published var translationOutput = ""
    @Published var translationSource = ""
    @Published var translationDirection = "en|zh-CN"
    @Published var translationProvider = ""
    @Published var translating = false
    @Published var accounts: [SavedAccount] = []
    @Published var activeAccountID = ""
    @Published var loggedIn = false
    @Published var accountStatus = "未登录"
    @Published var showLogin = false
    @Published var loginTarget: SavedAccount?
    @Published var notepads: [J] = []
    @Published var selectedPad = J.null
    @Published private(set) var selectingNotepad = false
    @Published var notebookRows: [J] = []
    @Published var notebookLoading = false
    @Published var todayStatus = ""
    @Published private(set) var todayStatusError = ""
    @Published var membership = false
    @Published var writing = false
    private struct NotepadWord: Hashable { let book: String; let word: String }
    struct HoverBookmarkTarget: Equatable { let account: Int; let id: String; let title: String }
    @Published private var pendingNotepadWords: Set<NotepadWord> = []
    var savingCurrentNotepadWord: Bool {
        pendingNotepadWords.contains(NotepadWord(book: preferences.selectedNotepad, word: word.lowercased()))
    }
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
    @Published var titlebar = TitlebarMetrics()
    var showWindow: (() -> Void)?
    var showQuickLookup: (() -> Void)?
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
    private var translationTask: Task<J, Error>?
    private var suggestionTask: Task<Void, Never>?
    private var lookupTask: Task<Void, Never>?
    private var accountTask: Task<Void, Never>?
    private var cloudNotesTask: Task<Void, Never>?
    private var studyStatusTask: Task<Void, Never>?
    private var nextStudyRetry: Date?
    private var notepadSelectionVersion = 0
    private var savingLogin = false
    private var preferencesObserver: AnyCancellable?
    private var preferencesSave: Task<Void, Never>?
    private var lastStubbornFetch: Date?
    private var nextStubbornRetry: Date?
    private var stubbornTask: Task<Void, Never>?
    private var stubbornSnapshot: StubbornSnapshot?
    private var nextAccountCheck: Date?
    private var pronunciationTask: Task<Void, Never>?
    private var pronunciationPlayer: AVAudioPlayer?

    init() {
        accounts = (load("accounts.json", as: [SavedAccount].self) ?? []).map { saved in
            var account = saved
            if account.localCredentialID == nil { account.needsLogin = true }
            return account
        }
        history = load("history.json", as: [SearchRecord].self) ?? []
        activeAccountID = UserDefaults.standard.string(forKey: "activeAccount") ?? ""
        core.onUnauthorized = { [weak self] in self?.expireAccount() }
        preferencesObserver = preferences.objectWillChange.sink { [weak self] _ in self?.schedulePreferencesSave() }
        updates = MacUpdates(preferences: preferences)
    }
    var activeAccount: SavedAccount? { accounts.first { $0.id == activeAccountID } }
    var titleAccount: String { activeAccount?.displayName ?? accountStatus }
    var stubbornCurrent: String { stubbornOrder.indices.contains(stubbornIndex) ? stubbornOrder[stubbornIndex]["spelling"].text : "" }
    var canBrowseStubborn: Bool { loggedIn || stubbornSnapshot != nil }
    var showingCachedStubborn: Bool { stubbornSnapshot != nil && (!loggedIn || lastStubbornFetch == nil) }
    var notepadConnectionHint: String? {
        if activeAccount?.needsLogin == true { return "请重新登录墨墨账号。" }
        switch accountStatus {
        case "正在连接": return "正在连接墨墨账号…"
        case "本机凭证读取失败": return "无法读取本机登录凭证，请在账号设置中重试。"
        case "暂时离线", "连接未完成": return "暂时无法连接墨墨，可稍后刷新云词本。"
        default: return loggedIn ? nil : "登录墨墨账号后即可使用。"
        }
    }
    var selectedNotepadTitle: String { selectedPad["title"].text }
    func initialize() async {
        Task { await restoreAccount() }
        do {
            let metadata = try await core.request("initialize")
            wordCount = metadata["wordCount"].integer; translationProvider = metadata["translationProvider"].text
            ready = true; status = metadata["warning"].text.isEmpty ? "词库已就绪。" : metadata["warning"].text
            Task {
                await refreshContextSentences()
                await refreshActiveSpellingMistakes()
            }
            if !preferences.onboardingCompleted { startTour() }
        } catch { fail(error) }
    }
    private func restoreAccount() async {
        guard let account = activeAccount else { return }
        await activate(account.id)
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
    func lookup(_ raw: String, navigate: Bool = true, recallMode enterRecall: Bool = false, keepPendingContext: Bool = false, keepSearchQuery: Bool = false) {
        let query = raw.trimmed
        guard ready, !query.isEmpty, query.count <= 100 else { return }
        stopPronunciation()
        if !keepPendingContext { pendingContext = nil }
        stopRecallMode()
        if page == .translation { cancelTranslation() }
        lookupTask?.cancel(); cancelCloudWordLookup(); wordVersion += 1
        let version = wordVersion; let account = accountVersion
        if !keepSearchQuery { search = query }
        showWindow?()
        lookupTask = Task {
            do {
                let chinese = query.range(of: "[\\p{Han}]", options: .regularExpression) != nil
                if chinese {
                    stopRecallMode()
                    chineseQuery = query
                    chineseHint = "正在查找相关词…"
                    chineseResults = []
                    chineseTotal = 0
                    page = .chinese; chineseLimit = 160
                    let result = try await core.request("dictionary.reverse", ["query": .string(query), "limit": .number(Double(chineseLimit))])
                    guard version == wordVersion, !Task.isCancelled else { return }
                    chineseResults = result["items"].list; chineseTotal = result["total"].integer
                    chineseHint = chineseTotal == 0
                        ? "未找到相关词。请缩短查询内容。"
                        : "共 \(chineseTotal) 个相关词。点击一行查看释义。"
                    status = "查词完成。"
                } else {
                    page = .word; word = query; entry = .null; notes = []; relations = .null; examples = []; spellingSuggestions = []; todayStatus = loggedIn ? "Loading" : ""
                    refreshMembership()
                    onlineDefinition = .null
                    definitionLoading = true
                    let result = try await core.request("dictionary.lookup", ["word": .string(query)])
                    guard version == wordVersion, !Task.isCancelled else { return }
                    word = result["word"].text
                    if keepPendingContext, let ctx = pendingContext {
                        pendingContext = PendingWordContext(word: word, matchedForm: ctx.matchedForm, sentence: ctx.sentence, translation: ctx.translation, source: ctx.source)
                    }
                    entry = result["entry"]
                    if account == accountVersion { notes = result["notes"].list }
                    if !keepSearchQuery { suggestions = result["suggestions"].list.map(\.text) }
                    spellingSuggestions = entry["translation"].text.trimmed.isEmpty ? result["spellingSuggestions"].list.map(\.text) : []
                    definitionLoading = entry["translation"].text.trimmed.isEmpty
                    let previouslyLookedUp = lookupCount(for: word) >= 1 || lookupCount(for: query) >= 1
                    recordHistory(word); status = definitionLoading ? "正在查找释义…" : "查词完成。"
                    let shouldRecall = enterRecall || (preferences.recallModeEnabled && navigate && !definitionLoading && previouslyLookedUp)
                    if shouldRecall { startRecallMode() }
                    Task { if let value = try? await core.request("dictionary.related", ["word": .string(word)]), version == wordVersion { relations = value } }
                    Task { await loadExamples(reset: true) }
                    loadCloudWord()
                }
                if navigate { appendLocation(.init(page: page, query: query)) }
                if !chinese && definitionLoading {
                    let result = try? await core.request("dictionary.online", ["word": .string(word)], timeout: 22)
                    guard version == wordVersion, page == .word, !Task.isCancelled else { return }
                    onlineDefinition = result ?? .null
                    definitionLoading = false
                    status = onlineDefinition == .null ? "暂无释义。" : "查词完成。"
                }
            } catch {
                if version == wordVersion, !Task.isCancelled {
                    if page == .chinese { chineseHint = "查词失败，请重试。" }
                    definitionLoading = false
                    fail(error)
                }
            }
        }
    }
    func moreChinese() async {
        let query = chineseQuery
        let version = wordVersion
        let limit = chineseLimit + 160
        guard let result = try? await core.request("dictionary.reverse", ["query": .string(query), "limit": .number(Double(limit))]),
              version == wordVersion, page == .chinese else { return }
        chineseLimit = limit
        chineseResults = result["items"].list
    }
    private func cancelCloudWordLookup() {
        cloudNotesTask?.cancel()
        studyStatusTask?.cancel()
        nextStudyRetry = nil
        todayStatusError = ""
    }
    private func loadCloudWord() {
        refreshMembership()
        cloudNotesTask?.cancel()
        guard loggedIn, page == .word, !word.isEmpty else { return }
        refreshTodayStatus()
        let query = word, version = wordVersion, generation = accountVersion
        cloudNotesTask = Task { [weak self] in
            guard let self, !Task.isCancelled else { return }
            if let result = try? await core.request("notes.cloud", ["word": .string(query)]),
               currentCloudWord(query, version: version, generation: generation) {
                notes = result.list
                markAccountConnected(generation)
            }
            guard currentCloudWord(query, version: version, generation: generation) else { return }
            if let phraseResult = try? await core.request("contextSentences.cloud", ["word": .string(query)]),
               currentCloudWord(query, version: version, generation: generation) {
                let items = phraseResult["items"].list
                if !items.isEmpty || phraseResult["items"] != .null {
                    contextSentences = items
                }
            }
        }
    }
    private func currentCloudWord(_ query: String, version: Int, generation: Int) -> Bool {
        !Task.isCancelled && loggedIn && page == .word && word == query && version == wordVersion && generation == accountVersion
    }
    func refreshTodayStatus() {
        guard loggedIn, page == .word, !word.isEmpty else { return }
        studyStatusTask?.cancel()
        todayStatus = "Loading"
        todayStatusError = ""
        nextStudyRetry = nil
        let query = word, version = wordVersion, generation = accountVersion
        studyStatusTask = Task { [weak self] in
            guard let self, !Task.isCancelled else { return }
            do {
                let result = try await core.request("study.status", ["word": .string(query)], timeout: 80)
                guard currentCloudWord(query, version: version, generation: generation) else { return }
                let state = result["status"].text
                guard !state.isEmpty else { throw CoreFailure(code: "study", message: "墨墨暂未返回学习状态，请稍后重试。") }
                todayStatus = state
                if state != "Missing" { markAccountConnected(generation) }
            } catch {
                guard currentCloudWord(query, version: version, generation: generation) else { return }
                todayStatus = "Failed"
                todayStatusError = error.localizedDescription
                if (error as? CoreFailure)?.code != "permission_denied" {
                    nextStudyRetry = Date().addingTimeInterval(45)
                }
            }
        }
    }
    func loadExamples(reset: Bool) async {
        if reset {
            if loadingExampleIndex != nil {
                stopPronunciation()
                if status == "正在加载例句语音…" { status = "已取消例句朗读" }
            }
            examplesVersion += 1; exampleOffset = 0; examples = []
        }
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
    var currentWordLookupCount: Int {
        lookupCount(for: word)
    }

    func lookupCount(for target: String) -> Int {
        let trimmed = target.trimmed
        guard !trimmed.isEmpty else { return 0 }
        return history.first { $0.word.caseInsensitiveCompare(trimmed) == .orderedSame }?.count ?? 0
    }

    var contextSentenceMap: [String: J] {
        var map: [String: J] = [:]
        for item in contextSentences {
            let key = item["word"].text.trimmed.lowercased()
            if !key.isEmpty, map[key] == nil {
                map[key] = item
            }
        }
        return map
    }

    func contextSentences(for targetWord: String) -> [J] {
        let trimmed = targetWord.trimmed
        guard !trimmed.isEmpty else { return [] }
        return contextSentences.filter { $0["word"].text.caseInsensitiveCompare(trimmed) == .orderedSame }
    }

    func isContextSentenceSaved(word targetWord: String, sentence: String) -> Bool {
        let w = targetWord.trimmed
        let s = sentence.trimmed.replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
        return contextSentences.contains {
            $0["word"].text.caseInsensitiveCompare(w) == .orderedSame &&
            $0["sentence"].text.trimmed.replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression).caseInsensitiveCompare(s) == .orderedSame
        }
    }

    func refreshContextSentences() async {
        if let result = try? await core.request("contextSentences.list") {
            contextSentences = result.list
        }
    }

    func refreshActiveSpellingMistakes() async {
        if let result = try? await core.request("spelling.active") {
            activeSpellingMistakes = result.list
        }
    }

    func saveContextSentence(word targetWord: String, sentence: String, translation: String, source: String, matchedForm: String? = nil) async {
        var params: [String: J] = [
            "word": .string(targetWord),
            "sentence": .string(sentence),
            "translation": .string(translation),
            "source": .string(source)
        ]
        if let matchedForm, !matchedForm.isEmpty { params["matchedForm"] = .string(matchedForm) }
        if (try? await core.request("contextSentences.save", params)) != nil {
            await refreshContextSentences()
            status = "已收藏「\(targetWord)」的例句"
        }
    }

    func removeContextSentence(id: String) async {
        _ = try? await core.request("contextSentences.remove", ["id": .string(id)])
        await refreshContextSentences()
        status = "已删除例句。"
    }

    func openContextSentenceEditor(_ item: J? = nil) {
        guard !word.trimmed.isEmpty else { return }
        editingContextSentence = item
        sheet = .contextEditor
    }

    func saveContextSentenceEdit(id: String?, sentence: String, translation: String, source: String, tags: [String], cloud: Bool, cloudID: String?) async throws {
        let query = word.trimmed
        guard !query.isEmpty else { return }
        let generation = accountVersion
        var params: [String: J] = [
            "word": .string(query),
            "sentence": .string(sentence),
            "translation": .string(translation),
            "source": .string(source),
            "tags": J.strings(tags)
        ]
        if let id, !id.isEmpty { params["id"] = .string(id) }
        if let cloudID, !cloudID.isEmpty { params["cloudId"] = .string(cloudID) }
        if cloud {
            do {
                let result = try await core.request("contextSentences.publish", params)
                if generation == accountVersion {
                    let items = result["items"].list
                    if !items.isEmpty || result["items"] != .null { contextSentences = items }
                    else { await refreshContextSentences() }
                }
                status = "例句已同步到墨墨云端。"
            } catch {
                await refreshContextSentences()
                throw error
            }
        } else {
            let result = try await core.request("contextSentences.update", params)
            let items = result["items"].list
            if !items.isEmpty || result["items"] != .null { contextSentences = items }
            else { await refreshContextSentences() }
            status = (cloudID?.isEmpty == false) ? "例句已保存到本地（尚未同步到云端）。" : "例句已保存到本地。"
        }
    }

    func deleteContextSentence(_ item: J, rethrowOnError: Bool = false) async throws -> Bool {
        let id = item["id"].text
        guard !id.isEmpty else { return false }
        let cloudID = item["cloudId"].text
        let hasCloudCopy = !cloudID.isEmpty
        if hasCloudCopy {
            guard confirm("删除云端例句", "这条例句已同步到墨墨云端。确认后将同时从本机和墨墨云端删除，手机端也会同步移除。", action: "确认删除") else {
                return false
            }
            guard loggedIn else {
                let msg = "当前未登录墨墨账号，无法删除云端例句。"
                status = msg
                if rethrowOnError { throw CoreFailure(code: "auth", message: msg) }
                return false
            }
            let generation = accountVersion
            do {
                let result = try await core.request("contextSentences.deleteCloud", [
                    "id": .string(id),
                    "cloudId": .string(cloudID),
                    "word": .string(item["word"].text.isEmpty ? word : item["word"].text)
                ])
                if generation == accountVersion {
                    let items = result["items"].list
                    if !items.isEmpty || result["items"] != .null { contextSentences = items }
                    else { await refreshContextSentences() }
                }
                status = "已从本机和墨墨云端删除例句。"
                return true
            } catch {
                let msg = "删除云端例句失败：\(error.localizedDescription)"
                status = msg
                if rethrowOnError { throw CoreFailure(code: "delete_cloud", message: msg) }
                return false
            }
        } else {
            await removeContextSentence(id: id)
            return true
        }
    }

    func recordContextReview(id: String) async {
        _ = try? await core.request("contextSentences.review", ["id": .string(id)])
        await refreshContextSentences()
    }

    func openContextReview(focusWord: String? = nil) {
        guard !contextSentences.isEmpty else {
            status = "还没有收藏例句，可在翻译或真题例句中点击「收藏例句」"
            return
        }
        contextReviewFocusWord = focusWord
        sheet = .contextReview
    }

    func lookupFromTranslation(_ rawWord: String) {
        let englishCorpus = translationDirection == "zh-CN|en" ? translationOutput : translationSource
        let chineseCorpus = translationDirection == "zh-CN|en" ? translationSource : translationOutput
        let sourceLabel = lastTranslationSourceLabel
        Task {
            if !englishCorpus.trimmed.isEmpty,
               let extracted = try? await core.request("contextSentences.extract", [
                    "word": .string(rawWord),
                    "matchedForm": .string(rawWord),
                    "paragraph": .string(englishCorpus),
                    "translation": .string(chineseCorpus)
               ]) {
                let sentence = extracted["sentence"].text
                let trans = extracted["translation"].text
                if !sentence.trimmed.isEmpty {
                    pendingContext = PendingWordContext(word: rawWord, matchedForm: rawWord, sentence: sentence, translation: trans, source: sourceLabel)
                }
            }
            lookup(rawWord, keepPendingContext: true)
        }
    }

    func saveFromTranslation(word targetWord: String) async {
        let englishCorpus = translationDirection == "zh-CN|en" ? translationOutput : translationSource
        let chineseCorpus = translationDirection == "zh-CN|en" ? translationSource : translationOutput
        guard !englishCorpus.trimmed.isEmpty else { return }
        if (try? await core.request("contextSentences.save", [
            "word": .string(targetWord),
            "matchedForm": .string(targetWord),
            "paragraph": .string(englishCorpus),
            "translation": .string(chineseCorpus),
            "source": .string(lastTranslationSourceLabel)
        ])) != nil {
            await refreshContextSentences()
            status = "已收藏「\(targetWord)」的例句，可随时例句自测"
        }
    }

    func startRecallMode() {
        recallTimerTask?.cancel()
        recallMode = true
        recallProgress = 0
        recallReady = false
        NSApp.keyWindow?.makeFirstResponder(nil)
        recallTimerTask = Task { @MainActor in
            let steps = 40
            for step in 1...steps {
                try? await Task.sleep(nanoseconds: 50_000_000)
                guard !Task.isCancelled, recallMode else { return }
                recallProgress = Double(step) / Double(steps)
            }
            if recallMode { recallReady = true }
        }
    }

    func stopRecallMode() {
        recallTimerTask?.cancel()
        recallTimerTask = nil
        recallMode = false
        recallProgress = 0
        recallReady = false
    }

    func revealFromRecall() {
        guard recallMode else { return }
        stopRecallMode()
        status = "已揭晓释义。"
    }

    func toggleRecallMode() {
        preferences.recallModeEnabled.toggle()
        if !preferences.recallModeEnabled && recallMode {
            stopRecallMode()
        }
        status = preferences.recallModeEnabled
            ? "已开启「先回想」模式，再次查询已查过的单词将先遮住释义 2 秒。"
            : "已关闭「先回想」模式。"
    }

    struct CacheCategoryInfo: Identifiable, Equatable {
        let id: String
        let title: String
        let description: String
        let summary: String
        let hasData: Bool
        let confirmMessage: String
    }

    private func formatCacheBytes(_ bytes: Int64) -> String {
        if bytes <= 0 { return "0 B" }
        if bytes < 1024 { return "\(bytes) B" }
        if bytes < 1024 * 1024 { return String(format: "%.1f KB", Double(bytes) / 1024.0) }
        return String(format: "%.2f MB", Double(bytes) / (1024.0 * 1024.0))
    }

    private func matchingProfileFiles(prefix: String, excluding: String? = nil) -> [URL] {
        guard let urls = try? FileManager.default.contentsOfDirectory(at: AppPaths.profile, includingPropertiesForKeys: [.fileSizeKey], options: [.skipsHiddenFiles]) else {
            return []
        }
        return urls.filter { url in
            let name = url.lastPathComponent
            guard name.hasPrefix(prefix), name.hasSuffix(".json") else { return false }
            if let excluding, name.hasPrefix(excluding) { return false }
            return true
        }
    }

    private func measureFilesBytes(_ urls: [URL]) -> Int64 {
        urls.reduce(Int64(0)) { sum, url in
            let size = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init) ?? 0
            return sum + size
        }
    }

    private func measureDirectoryBytes(_ dir: URL) -> Int64 {
        guard let enumerator = FileManager.default.enumerator(at: dir, includingPropertiesForKeys: [.fileSizeKey, .isRegularFileKey]) else {
            return 0
        }
        var total: Int64 = 0
        for case let url as URL in enumerator {
            if let vals = try? url.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey]), vals.isRegularFile == true {
                total += Int64(vals.fileSize ?? 0)
            }
        }
        return total
    }

    func loadCacheCategories() async -> [CacheCategoryInfo] {
        let stats = try? await core.request("cache.stats")
        let contextCount = stats?["contextCount"].integer ?? contextSentences.count
        let contextBytes = Int64(stats?["contextBytes"].doubleValue ?? 0)
        let spellingCount = stats?["spellingCount"].integer ?? activeSpellingMistakes.count
        let spellingBytes = Int64(stats?["spellingBytes"].doubleValue ?? 0)
        let memoryCount = stats?["memoryCount"].integer ?? 0
        let memoryBytes = Int64(stats?["memoryBytes"].doubleValue ?? 0)

        let historyCount = history.count
        let flashcardFiles = matchingProfileFiles(prefix: "flashcards-")
        let flashcardBytes = measureFilesBytes(flashcardFiles)
        let stubbornFiles = matchingProfileFiles(prefix: "stubborn-")
        let stubbornBytes = measureFilesBytes(stubbornFiles)
        let updatesBytes = measureDirectoryBytes(AppPaths.profile.appendingPathComponent("updates", isDirectory: true))
        let networkBytes = memoryBytes + updatesBytes
        let hasNetwork = memoryCount > 0 || updatesBytes > 0
        let networkSummary: String
        if !hasNetwork {
            networkSummary = "暂无缓存"
        } else if memoryCount > 0 && networkBytes > 0 {
            networkSummary = "\(memoryCount) 项缓存 · \(formatCacheBytes(networkBytes))"
        } else if memoryCount > 0 {
            networkSummary = "\(memoryCount) 项缓存"
        } else {
            networkSummary = formatCacheBytes(networkBytes)
        }

        return [
            CacheCategoryInfo(
                id: "history",
                title: "查词历史与次数",
                description: "本机查词记录及查词次数，清除后「最近查词」和重复次数标记会重置。",
                summary: historyCount > 0 ? "\(historyCount) 条记录" : "暂无记录",
                hasData: historyCount > 0,
                confirmMessage: "将清空本机的全部查词历史与次数统计，不影响云词本和账号登录。"
            ),
            CacheCategoryInfo(
                id: "contexts",
                title: "我的例句（本机缓存）",
                description: "本机保存与缓存的例句及自测次数，不影响墨墨云端例句。",
                summary: contextCount > 0 ? "\(contextCount) 条例句 · \(formatCacheBytes(contextBytes))" : "暂无数据",
                hasData: contextCount > 0,
                confirmMessage: "仅清空本机保存与缓存的例句，不影响已同步到墨墨云端的例句。"
            ),
            CacheCategoryInfo(
                id: "spelling",
                title: "拼写易错记录",
                description: "拼写默写中记住的易错位置与针对练习记录。",
                summary: spellingCount > 0 ? "\(spellingCount) 个单词 · \(formatCacheBytes(spellingBytes))" : "暂无记录",
                hasData: spellingCount > 0,
                confirmMessage: "将清空已记录的拼写易错点，后续拼写练习会重新记录。"
            ),
            CacheCategoryInfo(
                id: "flashcards",
                title: "卡片自测进度",
                description: "顽固单词与云词本的卡片自测进度。",
                summary: !flashcardFiles.isEmpty ? "\(flashcardFiles.count) 份进度 · \(formatCacheBytes(flashcardBytes))" : "暂无进度",
                hasData: !flashcardFiles.isEmpty,
                confirmMessage: "将清除所有本地卡片自测进度，下次打开将从第 1 组开始。"
            ),
            CacheCategoryInfo(
                id: "stubborn",
                title: "顽固单词缓存",
                description: "保存在本机的顽固单词列表，清除后联网会自动重新同步。",
                summary: !stubbornFiles.isEmpty ? "\(stubbornFiles.count) 份缓存 · \(formatCacheBytes(stubbornBytes))" : "暂无缓存",
                hasData: !stubbornFiles.isEmpty,
                confirmMessage: "将清除本机缓存的顽固单词列表，不会退出账号，联网后会自动重新同步。"
            ),
            CacheCategoryInfo(
                id: "network",
                title: "网络与临时缓存",
                description: "真题例句、在线释义、翻译、语音缓存及临时文件。",
                summary: networkSummary,
                hasData: hasNetwork,
                confirmMessage: "将清除在线查询、翻译与语音缓存及临时文件，下次使用时重新获取。"
            )
        ]
    }

    func clearCacheCategory(_ id: String) async -> String {
        switch id {
        case "history":
            clearHistory()
            return "已清除查词历史与次数。"
        case "contexts":
            _ = try? await core.request("contextSentences.clear")
            await refreshContextSentences()
            return "已清除收藏的例句。"
        case "spelling":
            _ = try? await core.request("spelling.clear")
            await refreshActiveSpellingMistakes()
            return "已清除拼写易错记录。"
        case "flashcards":
            for url in matchingProfileFiles(prefix: "flashcards-") {
                try? FileManager.default.removeItem(at: url)
            }
            return "已清除所有卡片自测进度。"
        case "stubborn":
            for url in matchingProfileFiles(prefix: "stubborn-") {
                try? FileManager.default.removeItem(at: url)
            }
            stubbornSnapshot = nil
            stubbornWords = []
            stubbornOrder = []
            stubbornIndex = 0
            stubbornTotal = 0
            stubbornHint = ""
            lastStubbornFetch = nil
            return "已清除顽固单词缓存。"
        case "network":
            _ = try? await core.request("cache.clear")
            let updatesDir = AppPaths.profile.appendingPathComponent("updates", isDirectory: true)
            if let children = try? FileManager.default.contentsOfDirectory(at: updatesDir, includingPropertiesForKeys: nil) {
                for item in children {
                    try? FileManager.default.removeItem(at: item)
                }
            }
            return "已清除网络与临时缓存。"
        default:
            return ""
        }
    }

    func clearAllCaches() async -> String {
        let active = await loadCacheCategories().filter(\.hasData)
        guard !active.isEmpty else { return "当前没有可清除的缓存。" }
        for item in active {
            _ = await clearCacheCategory(item.id)
        }
        return "已清除全部 \(active.count) 类本机缓存与记录。"
    }

    func openMorphemePractice(form: String, meaning: String, currentWord: String) {
        Task {
            var priority = Set<String>()
            for h in history where !h.word.trimmed.isEmpty { priority.insert(h.word) }
            for s in stubbornWords {
                let sp = s["spelling"].text.trimmed
                if !sp.isEmpty { priority.insert(sp) }
            }
            for line in selectedPad["content"].text.components(separatedBy: .newlines) {
                let w = line.trimmed
                if !w.isEmpty && !w.hasPrefix("#") { priority.insert(w) }
            }
            let result = try? await core.request("mnemonic.practiceGroup", [
                "form": .string(form),
                "meaning": .string(meaning),
                "currentWord": .string(currentWord),
                "priorityWords": J.strings(Array(priority)),
                "limit": .number(6)
            ])
            if let result, result != .null, !result["members"].list.isEmpty {
                morphemePracticeGroup = result
                sheet = .morphemePractice
            } else {
                status = "「\(form)」同根词数量不足，暂无法组成辨析练习。"
            }
        }
    }

    func speak(_ accent: EnglishAccent) {
        guard page == .word, !word.isEmpty else { return }
        playSpeech(word, accent: accent)
    }

    func speakWord(_ targetWord: String, accent: EnglishAccent = .american) {
        let trimmed = targetWord.trimmed
        guard !trimmed.isEmpty else { return }
        playSpeech(trimmed, accent: accent, requireWordPage: false)
    }

    func speakExample(at index: Int) {
        guard page == .word, examples.indices.contains(index), loadingExampleIndex != index else { return }
        let text = examples[index]["english"].text
        guard !text.trimmed.isEmpty else { return }
        playSpeech(text, accent: .american, exampleIndex: index)
    }

    func speakSentence(_ text: String) {
        let trimmed = text.trimmed
        guard !trimmed.isEmpty else { return }
        playSpeech(trimmed, accent: .american, isSentenceOverride: true)
    }

    private func playSpeech(_ text: String, accent: EnglishAccent, exampleIndex: Int? = nil, requireWordPage: Bool = true, isSentenceOverride: Bool = false) {
        stopPronunciation()
        loadingExampleIndex = exampleIndex
        let isSentence = isSentenceOverride || exampleIndex != nil
        let version = wordVersion
        let label = isSentence ? "例句" : accent.label
        let method = isSentence ? "examples.pronunciation" : "pronunciation"
        let parameters: [String: J] = isSentence ? ["text": .string(text)] : ["word": .string(text), "accent": .string(accent.rawValue)]
        status = isSentence ? "正在加载例句语音…" : "正在加载发音…"
        pronunciationTask = Task { [weak self] in
            guard let self, !Task.isCancelled else { return }
            let result = try? await core.request(method, parameters, timeout: isSentence ? 19 : 7)
            guard !Task.isCancelled else { return }
            pronunciationTask = nil
            loadingExampleIndex = nil
            if requireWordPage { guard page == .word, version == wordVersion else { return } }
            if let encoded = result?["audio"].text, !encoded.isEmpty, let audio = Data(base64Encoded: encoded),
               let player = try? AVAudioPlayer(data: audio), player.play() {
                pronunciationPlayer = player
                status = "正在播放\(label)。"
            } else {
                status = "在线语音暂不可用，请稍后重试。"
            }
        }
    }

    func lookupMorphemeFamily(form: String, meaning: String, excludeWord: String) async -> [J] {
        guard let result = try? await core.request("mnemonic.family", [
            "form": .string(form),
            "meaning": .string(meaning),
            "excludeWord": .string(excludeWord),
            "limit": .number(18)
        ]) else { return [] }
        return result.list
    }

    func stopPronunciation() {
        pronunciationTask?.cancel()
        pronunciationTask = nil
        if loadingExampleIndex != nil { loadingExampleIndex = nil }
        pronunciationPlayer?.stop()
        pronunciationPlayer = nil
    }
    func enterTranslation() {
        stopPronunciation()
        stopRecallMode()
        lookupTask?.cancel()
        cancelCloudWordLookup()
        page = .translation
        appendLocation(.init(page: .translation, query: "段落翻译", input: translationInput, translated: translationOutput, direction: translationDirection))
    }
    func translate() async {
        guard !translationInput.trimmed.isEmpty else { return }
        cancelTranslation()
        let version = translationVersion
        let input = translationInput, direction = translationDirection
        translating = true; translationSource = input; translationOutput = ""; translationProvider = ""; extractedWords = []
        let request = Task { try await core.request("translate", ["text": .string(input), "direction": .string(direction)], timeout: 22) }
        translationTask = request
        do {
            let result = try await request.value
            guard version == translationVersion else { return }
            translationOutput = result["text"].text; translationProvider = result["provider"].text
            if navigation.indices.contains(navigationIndex), page == .translation { navigation[navigationIndex] = .init(page: .translation, query: "段落翻译", input: input, translated: translationOutput, direction: direction) }
            let englishCorpus = direction == "zh-CN|en" ? translationOutput : input
            if let extracted = try? await core.request("translation.extract", ["text": .string(englishCorpus), "limit": .number(24)]), version == translationVersion {
                extractedWords = extracted.list
            }
        } catch { if version == translationVersion { fail(error) } }
        if version == translationVersion { translating = false; translationTask = nil }
    }
    private func cancelTranslation() {
        translationVersion += 1
        translationTask?.cancel()
        translationTask = nil
        translating = false
    }
    func classify(_ text: String, foreground: Bool = true, sourceLabel: String = "段落翻译") async {
        do {
            let result = try await core.request("text.classify", ["text": .string(text)])
            if foreground { showWindow?() }
            switch result["kind"].text {
            case "Word", "Chinese": lookup(result["text"].text)
            case "Paragraph": lastTranslationSourceLabel = sourceLabel; translationInput = result["text"].text; translationDirection = result["direction"].text; enterTranslation(); await translate()
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
        if page == .translation { cancelTranslation() }
        navigationIndex = target; let location = navigation[target]; page = location.page
        if page == .translation {
            stopPronunciation()
            lookupTask?.cancel()
            translationInput = location.input; translationSource = location.input; translationOutput = location.translated; translationDirection = location.direction
        }
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

    func activate(_ id: String, verified: Bool = false) async {
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
            if let name = stubbornCacheName(id), let snapshot = load(name, as: StubbornSnapshot.self),
               snapshot.total >= snapshot.words.count {
                installStubborn(snapshot)
            }
            guard let credentialID = account.localCredentialID, !account.needsLogin else {
                accountStatus = "请重新登录"
                await clearWorkerAccount(generation)
                return
            }
            if let expiry = account.expiresAt, expiry <= Date() { expireAccount(); return }
            let credential = try await CredentialVault.shared.read(credentialID)
            guard generation == accountVersion else { return }
            guard let credential else { expireAccount(missingCredential: true); return }
            try await core.setAccount(token: credential)
            guard generation == accountVersion else { return }
            loggedIn = true
            Task { await refreshContextSentences() }
            let query = word
            if !query.isEmpty {
                Task {
                    if let result = try? await core.request("notes.local", ["word": .string(query)]),
                       generation == accountVersion, query == word, notes.isEmpty { notes = result.list }
                }
            }
            if verified { markAccountConnected(generation) }
            loadCloudWord()
            checkAccountConnection(generation)
        } catch {
            guard generation == accountVersion else { return }
            if error is CredentialStoreError {
                accountStatus = "本机凭证读取失败"
                status = error.localizedDescription
                await clearWorkerAccount(generation)
                return
            }
            accountStatus = "连接未完成"
            status = error.localizedDescription
            nextAccountCheck = Date().addingTimeInterval(45)
        }
    }
    private func markAccountConnected(_ generation: Int) {
        guard generation == accountVersion, loggedIn else { return }
        if accountStatus != "已登录" { accountStatus = "已登录" }
        nextAccountCheck = nil
    }
    private func checkAccountConnection(_ generation: Int) {
        accountTask = Task { [weak self] in
            guard let self else { return }
            await refreshNotepads()
            guard generation == accountVersion, !Task.isCancelled else { return }
            if accountStatus != "已登录" {
                do {
                    _ = try await core.request("account.validate", timeout: 55)
                    guard !Task.isCancelled else { return }
                    markAccountConnected(generation)
                } catch {
                    guard generation == accountVersion, !Task.isCancelled, accountStatus != "已登录" else { return }
                    accountStatus = "暂时离线"
                    status = error.localizedDescription
                    nextAccountCheck = Date().addingTimeInterval(45)
                    return
                }
            }
            guard generation == accountVersion, !Task.isCancelled else { return }
            await refreshStubborn(force: true)
        }
    }
    func finishLogin(_ authorization: J, identity: String, fromOfficialPage: Bool = false) async throws {
        let token = authorization["token"].text
        guard !token.isEmpty else { throw CoreFailure(code: "login", message: "未能获取登录信息，请重新登录。") }
        guard !savingLogin else { throw CoreFailure(code: "login", message: "正在保存墨墨账号，请稍候。") }
        savingLogin = true
        defer { savingLogin = false }
        let targetID = fromOfficialPage ? loginTarget?.id : nil
        if !fromOfficialPage { _ = try await core.request("login.validate", ["token": .string(token)]) }
        let identity = identity.trimmed.lowercased()
        var sameCredential: SavedAccount?
        for account in accounts where !account.needsLogin {
            try Task.checkCancellation()
            if let credentialID = account.localCredentialID,
               (try? await CredentialVault.shared.read(credentialID)) == token {
                sameCredential = account
                break
            }
        }
        try Task.checkCancellation()
        let sameIdentity = identity.isEmpty ? nil : accounts.first { $0.identity == identity }
        let matched = sameIdentity ?? sameCredential
        let target = targetID.flatMap { id in accounts.first { $0.id == id } }
        if let sameCredential, let sameIdentity, sameCredential.id != sameIdentity.id {
            throw CoreFailure(code: "login", message: "本次登录与另一个已保存的账号冲突，请重新登录对应账号。")
        }
        if let sameCredential, !sameCredential.identity.isEmpty, !identity.isEmpty, sameCredential.identity != identity {
            throw CoreFailure(code: "login", message: "本次登录与另一个已保存的账号冲突，请重新登录对应账号。")
        }
        if targetID != nil {
            guard let target else { throw CoreFailure(code: "login", message: "原账号已移除，请重新添加账号。") }
            if let matched, matched.id != target.id {
                throw CoreFailure(code: "login", message: "当前登录的墨墨账号与所选账号不同，请重新登录对应账号。")
            }
            if matched == nil, !target.identity.isEmpty {
                throw CoreFailure(code: "login", message: "暂时无法确认是所选账号，请使用该账号重新登录。")
            }
        }
        let existing = matched ?? target
        let id = existing?.id ?? UUID().uuidString
        let credentialID = UUID()
        try await CredentialVault.shared.add(token, account: credentialID)
        if Task.isCancelled {
            try? await CredentialVault.shared.remove(credentialID)
            throw CancellationError()
        }
        let resolvedIdentity = identity.isEmpty ? (existing?.identity ?? "") : identity
        let isPlaceholder = existing?.name.range(of: "^墨墨账号 [0-9]+$", options: .regularExpression) != nil
        let name: String
        if let existing, !isPlaceholder || resolvedIdentity.isEmpty {
            name = existing.displayName
        } else {
            name = resolvedIdentity.isEmpty ? "墨墨账号 \(accounts.count + 1)" : SavedAccount.displayIdentity(resolvedIdentity)
        }
        let account = SavedAccount(id: id, name: name, identity: resolvedIdentity,
                                   expiresAt: parseISODate(authorization["expiresAt"].text), localCredentialID: credentialID)
        var updatedAccounts = accounts
        if let index = updatedAccounts.firstIndex(where: { $0.id == id }) { updatedAccounts[index] = account }
        else if existing != nil {
            try? await CredentialVault.shared.remove(credentialID)
            throw CoreFailure(code: "login", message: "原账号已移除，请重新添加账号。")
        }
        else { updatedAccounts.append(account) }
        do { try writeProfile(updatedAccounts, "accounts.json") }
        catch {
            try? await CredentialVault.shared.remove(credentialID)
            throw CredentialStoreError.save
        }
        accounts = updatedAccounts
        loginTarget = nil
        showLogin = false
        if let previous = existing?.localCredentialID { Task { try? await CredentialVault.shared.remove(previous) } }
        Task { await activate(id, verified: !fromOfficialPage) }
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
    func expireAccount(missingCredential: Bool = false) {
        guard let index = accounts.firstIndex(where: { $0.id == activeAccountID }), !accounts[index].needsLogin else { return }
        let credentialID = accounts[index].localCredentialID
        accounts[index].needsLogin = true
        if let credentialID, !missingCredential { Task { try? await CredentialVault.shared.remove(credentialID) } }
        save(accounts, "accounts.json")
        accountVersion += 1
        let generation = accountVersion
        accountTask?.cancel()
        loggedIn = false
        accountStatus = missingCredential ? "请重新登录" : "登录已失效"
        clearCloudState(keepStubborn: true)
        Task { await clearWorkerAccount(generation) }
        let title = missingCredential ? "请重新登录墨墨账号" : "墨墨登录已失效"
        notice = .init(title: title, message: "请重新登录或导入有效的墨墨 API 授权。登录后，云词本和顽固单词会自动刷新。", confirm: "账号设置", action: { [weak self] in self?.openAccountSettings() })
    }
    private func clearWorkerAccount(_ generation: Int) async {
        guard generation == accountVersion else { return }
        try? await core.setAccount(token: nil)
        await refreshContextSentences()
        let query = word
        guard !query.isEmpty, let value = try? await core.request("notes.local", ["word": .string(query)]),
              generation == accountVersion, query == word else { return }
        notes = value.list
    }
    func checkExpiry() {
        guard let account = activeAccount, !account.needsLogin else { return }
        if let expiry = account.expiresAt, expiry <= Date() { expireAccount(); return }
        if let retry = nextAccountCheck, retry <= Date() {
            nextAccountCheck = nil
            if loggedIn { checkAccountConnection(accountVersion) }
            else { Task { await activate(account.id) } }
        }
        if loggedIn, !writing, todayStatus == "Failed", let retry = nextStudyRetry, retry <= Date() {
            refreshTodayStatus()
        }
    }
    func renameAccount(_ id: String, _ name: String) {
        guard let index = accounts.firstIndex(where: { $0.id == id }), !name.trimmed.isEmpty else { return }
        accounts[index].name = String(name.trimmed.prefix(80)); save(accounts, "accounts.json")
    }
    func removeAccount(_ id: String) async {
        guard let account = accounts.first(where: { $0.id == id }) else { return }
        if let credentialID = account.localCredentialID {
            do { try await CredentialVault.shared.remove(credentialID) }
            catch { fail(error); return }
        }
        guard accounts.contains(where: { $0.id == id && $0.localCredentialID == account.localCredentialID }) else { return }
        accounts.removeAll { $0.id == id }
        save(accounts, "accounts.json")
        if let name = stubbornCacheName(id) { try? FileManager.default.removeItem(at: AppPaths.profile.appendingPathComponent(name)) }
        try? FileManager.default.removeItem(at: AppPaths.profile.appendingPathComponent(flashcardProgressName(id)))
        if activeAccountID == id { await logout() }
    }
    private func clearCloudState(keepStubborn: Bool = false) {
        cancelCloudWordLookup()
        core.invalidateAccount()
        notepadSelectionVersion += 1
        selectingNotepad = false
        if sheet == .notepad { sheet = nil }
        notes = []; notepads = []; selectedPad = .null; notebookRows = []; notebookLoading = false
        membership = false; todayStatus = ""; writing = false; pendingNotepadWords.removeAll()
        stubbornTask?.cancel(); stubbornTask = nil; stubbornLoading = false
        if !keepStubborn {
            stubbornSnapshot = nil
            stubbornWords = []; stubbornOrder = []; stubbornIndex = 0; stubbornTotal = 0
        }
        stubbornHint = ""; lastStubbornFetch = nil; nextStubbornRetry = nil; nextAccountCheck = nil
    }
    func refreshNotepads() async {
        guard loggedIn else { return }; let generation = accountVersion
        do {
            let result = try await core.request("notepads.list")
            guard generation == accountVersion, !Task.isCancelled else { return }
            markAccountConnected(generation)
            notepads = result.list
            let id = notepads.contains { $0["id"].text == preferences.selectedNotepad } ? preferences.selectedNotepad : notepads.first?["id"].text ?? ""
            preferences.selectedNotepad = id; preferences.save(); await selectNotepad(id)
        } catch { if generation == accountVersion, !Task.isCancelled { status = error.localizedDescription } }
    }
    func selectNotepad(_ id: String) async {
        guard loggedIn else { return }
        notepadSelectionVersion += 1
        let version = notepadSelectionVersion, generation = accountVersion
        if preferences.selectedNotepad != id { preferences.selectedNotepad = id; preferences.save() }
        if selectedPad["id"].text != id || id.isEmpty { selectedPad = .null; refreshMembership() }
        selectingNotepad = !id.isEmpty
        guard selectingNotepad else { return }
        defer { if version == notepadSelectionVersion { selectingNotepad = false } }
        do {
            let value = try await core.request("notepads.get", ["id": .string(id)])
            guard generation == accountVersion, version == notepadSelectionVersion, !Task.isCancelled else { return }
            selectedPad = value
            refreshMembership()
        } catch { if version == notepadSelectionVersion, !Task.isCancelled { status = error.localizedDescription } }
    }
    func refreshMembership() {
        membership = !word.isEmpty && selectedPad["id"].text == preferences.selectedNotepad
            && selectedPad["content"].text.components(separatedBy: .newlines).contains {
                !$0.trimmed.hasPrefix("#") && $0.trimmed.caseInsensitiveCompare(word) == .orderedSame
            }
    }
    var hoverBookmarkTarget: HoverBookmarkTarget? {
        let id = selectedPad["id"].text
        guard loggedIn, !selectingNotepad, !id.isEmpty, id == preferences.selectedNotepad,
              selectedPad["type"].text != "FAVORITE" else { return nil }
        return .init(account: accountVersion, id: id, title: selectedPad["title"].text)
    }

    func hoverBookmarkStatus(_ word: String, target: HoverBookmarkTarget?) -> (saved: Bool, pending: Bool) {
        guard let target, target == hoverBookmarkTarget else { return (false, false) }
        let saved = selectedPad["content"].text.components(separatedBy: .newlines).contains {
            !$0.trimmed.hasPrefix("#") && $0.trimmed.caseInsensitiveCompare(word) == .orderedSame
        }
        return (saved, pendingNotepadWords.contains(.init(book: target.id, word: word.lowercased())))
    }

    func saveHoverBookmark(_ word: String, target: HoverBookmarkTarget) async throws {
        guard target == hoverBookmarkTarget else {
            throw CoreFailure(code: "notepad", message: "云词本已切换，请重试")
        }
        if hoverBookmarkStatus(word, target: target).saved { return }
        let key = NotepadWord(book: target.id, word: word.lowercased())
        guard pendingNotepadWords.insert(key).inserted else { return }
        defer { if target.account == accountVersion { pendingNotepadWords.remove(key) } }
        let result = try await core.request("notepads.addHover", ["id": .string(target.id), "word": .string(word)])
        guard target.account == accountVersion, !Task.isCancelled else { throw CancellationError() }
        if preferences.selectedNotepad == target.id, result["notepad"]["id"].text == target.id {
            selectedPad = result["notepad"]
            refreshMembership()
        }
        status = "「\(word)」已在「\(target.title)」中"
    }
    func toggleMembership() async {
        guard loggedIn else { openAccountSettings(); return }
        guard !word.isEmpty, !writing, !selectingNotepad else { return }
        let id = selectedPad["id"].text
        guard !id.isEmpty, preferences.selectedNotepad == id else {
            if notepads.isEmpty, preferences.selectedNotepad.isEmpty { createNotebook() }
            else { status = "请先刷新并选择云词本。" }
            return
        }
        let generation = accountVersion, query = word, removing = membership
        let key = NotepadWord(book: id, word: query.lowercased())
        guard pendingNotepadWords.insert(key).inserted else { return }
        defer { if generation == accountVersion { pendingNotepadWords.remove(key) } }
        do {
            let result = try await core.request(removing ? "notepads.remove" : "notepads.add", ["id": .string(id), "word": .string(query)])
            guard generation == accountVersion else { return }
            if preferences.selectedNotepad == id {
                selectedPad = result["notepad"]
                refreshMembership()
            }
            status = removing ? "已从云词本移除「\(query)」" : "已将「\(query)」加入云词本"
        } catch let error as CoreFailure where error.code == "empty_notepad" {
            guard generation == accountVersion, preferences.selectedNotepad == id else { return }
            await selectNotepad(id)
            guard generation == accountVersion, preferences.selectedNotepad == id else { return }
            status = "云词本至少需要保留一个单词。"
            notice = .init(title: "无法移除最后一个单词", message: status)
        } catch { if generation == accountVersion { fail(error) } }
    }
    func addToday() {
        guard loggedIn else { openAccountSettings(); return }
        guard !word.isEmpty, !writing else { return }
        switch todayStatus {
        case "Failed":
            refreshTodayStatus()
        case "SyncRequired", "PendingVerification":
            Task { await performAddToday(verifyOnly: true) }
        case "Loading", "AlreadyLearned", "AlreadyToday", "Added", "Planned", "Missing":
            return
        default:
            let query = word, version = wordVersion, generation = accountVersion
            notice = .init(title: "将「\(query)」添加到今日新学？",
                message: "学习状态依赖墨墨的数据同步。开启手机端自动同步，可及时更新学习记录。\n\n墨墨背单词 → 我的 → 学习数据同步 → 学习数据自动同步",
                confirm: "添加", action: { [weak self] in
                    Task {
                        guard let self, self.currentCloudWord(query, version: version, generation: generation) else { return }
                        await self.performAddToday()
                    }
                })
        }
    }
    private func performAddToday(verifyOnly: Bool = false) async {
        guard loggedIn, !writing, !word.isEmpty else { return }; writing = true; let generation = accountVersion; let query = word
        do {
            let result = try await core.request("study.add", ["word": .string(query), "verifyOnly": .bool(verifyOnly)])
            if generation == accountVersion, query == word {
                todayStatus = result["status"].text
                switch todayStatus {
                case "Added": status = "「\(query)」已加入今日新学。"
                case "AlreadyToday": status = "「\(query)」已计划新学，不会重复添加。"
                case "Planned": status = "「\(query)」已计划新学，手机端同步后可查看今日安排。"
                case "AlreadyLearned": status = "「\(query)」已学过或已安排今日复习，无需再加入新学。"
                case "SyncRequired": status = "「\(query)」的学习记录还未同步，请在墨墨 App 同步后点击核对。"
                case "NotAdded": status = "墨墨本次未添加「\(query)」。"
                default: status = "「\(query)」是否加入今日新学还未确认，请在墨墨 App 同步后点击核对。"
                }
            }
        }
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
    func batchAddToNotepad(_ words: [String]) async throws -> Int {
        guard loggedIn else { openAccountSettings(); throw CoreFailure(code: "auth", message: "请先登录墨墨账号。") }
        let id = selectedPad["id"].text
        guard !id.isEmpty, id == preferences.selectedNotepad else {
            throw CoreFailure(code: "notepad", message: "请先在左侧选择一个云词本。")
        }
        let generation = accountVersion
        let result = try await core.request("notepads.addWords", ["id": .string(id), "words": J.strings(words)])
        guard generation == accountVersion else { return 0 }
        if preferences.selectedNotepad == id, result["notepad"]["id"].text == id {
            selectedPad = result["notepad"]
            refreshMembership()
        }
        return result["added"].integer
    }
    func saveNotebook(original: J, title: String, brief: String, content: String) async throws {
        let generation = accountVersion
        let result = try await core.request("notepads.update", ["original": original, "title": .string(title), "brief": .string(brief), "content": .string(content)])
        guard generation == accountVersion else { return }; selectedPad = result; await loadNotebookRows(); refreshMembership(); await refreshNotepads()
    }
    func deleteNotebook(original: J) async throws {
        let id = original["id"].text
        guard loggedIn, !writing, !selectingNotepad, !id.isEmpty, id == selectedPad["id"].text, id == preferences.selectedNotepad else {
            throw CoreFailure(code: "notepad", message: "当前词本已变更，请重新载入后再确认。")
        }
        let generation = accountVersion
        writing = true
        defer { if generation == accountVersion { writing = false } }
        _ = try await core.request("notepads.delete", ["original": original])
        guard generation == accountVersion else { return }
        if preferences.selectedNotepad == id { selectedPad = .null; refreshMembership() }
        sheet = nil
        await refreshNotepads()
        if generation == accountVersion { status = "云词本已删除。" }
    }
    func refreshStubborn(force: Bool = false) async {
        guard loggedIn, !stubbornLoading else { return }
        if !force, let retry = nextStubbornRetry, retry > Date() { return }
        if !force, let fetched = lastStubbornFetch, Date().timeIntervalSince(fetched) < 900 { return }
        let generation = accountVersion
        let cacheName = stubbornCacheName(activeAccountID)
        stubbornLoading = true
        stubbornHint = ""
        stubbornTask = Task {
            defer {
                if generation == accountVersion { stubbornLoading = false; stubbornTask = nil }
            }
            do {
                let result = try await core.request("stubborn")
                guard generation == accountVersion, !Task.isCancelled else { return }
                guard case .array(let words) = result["words"], case .number(let count) = result["total"],
                      let total = Int(exactly: count), total >= words.count,
                      case .bool(let complete) = result["complete"] else {
                    throw CoreFailure(code: "stubborn", message: "顽固单词数据不完整，请稍后刷新。")
                }
                lastStubbornFetch = Date()
                nextStubbornRetry = nil
                if !complete, stubbornSnapshot?.complete == true {
                    stubbornHint = "本次只收到 \(words.count)/\(total) 个单词，仍显示上次的列表。"
                    return
                }
                let snapshot = StubbornSnapshot(words: words, total: total, complete: complete)
                if let cacheName { save(snapshot, cacheName) }
                installStubborn(snapshot)
            } catch {
                guard generation == accountVersion, !Task.isCancelled else { return }
                lastStubbornFetch = nil
                nextStubbornRetry = Date().addingTimeInterval(65)
                stubbornHint = stubbornSnapshot == nil ? error.localizedDescription : "刷新未完成，仍显示上次的列表。\(error.localizedDescription)"
            }
        }
        await stubbornTask?.value
    }
    private func stubbornCacheName(_ accountID: String) -> String? {
        UUID(uuidString: accountID).map { "stubborn-\($0.uuidString).json" }
    }
    private func flashcardProgressName(_ accountID: String, scope: String? = nil) -> String {
        if let scope, !scope.isEmpty {
            let safe = scope.filter { $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }
            return "flashcards-scope-\(safe.isEmpty ? "default" : safe).json"
        }
        if let uuid = UUID(uuidString: accountID) { return "flashcards-\(uuid.uuidString).json" }
        return "flashcards-default.json"
    }
    func loadFlashcardProgress(scope: String? = nil) -> FlashcardProgressSnapshot? {
        load(flashcardProgressName(activeAccountID, scope: scope), as: FlashcardProgressSnapshot.self)
    }
    func saveFlashcardProgress(_ snapshot: FlashcardProgressSnapshot, scope: String? = nil) {
        save(snapshot, flashcardProgressName(activeAccountID, scope: scope))
    }
    func clearFlashcardProgress(scope: String? = nil) {
        try? FileManager.default.removeItem(at: AppPaths.profile.appendingPathComponent(flashcardProgressName(activeAccountID, scope: scope)))
    }
    private func installStubborn(_ snapshot: StubbornSnapshot) {
        let current = stubbornCurrent
        let previous = stubbornOrder.map { $0["spelling"].text.lowercased() }
        var seen = Set<String>()
        stubbornSnapshot = snapshot
        stubbornWords = snapshot.words.filter {
            let word = $0["spelling"].text
            return !word.isEmpty && word.count <= 100 && seen.insert(word.lowercased()).inserted
        }
        stubbornTotal = snapshot.total
        stubbornHint = snapshot.complete ? "" : "已获取 \(stubbornWords.count)/\(stubbornTotal) 个单词。"
        if preferences.stubbornSort == "random", Set(previous) == seen {
            let updated = Dictionary(uniqueKeysWithValues: stubbornWords.map { ($0["spelling"].text.lowercased(), $0) })
            stubbornOrder = previous.compactMap { updated[$0] }
            stubbornIndex = stubbornOrder.firstIndex { $0["spelling"].text.caseInsensitiveCompare(current) == .orderedSame } ?? 0
        } else {
            reorderStubborn(keeping: current)
        }
    }
    func reorderStubborn(keeping current: String = "") {
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
        if let index = stubbornOrder.firstIndex(where: { $0["spelling"].text.caseInsensitiveCompare(current) == .orderedSame }) {
            if preferences.stubbornSort == "random" { stubbornOrder.swapAt(0, index) }
            else { stubbornIndex = index }
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
    private func save<T: Encodable>(_ value: T, _ name: String) {
        do { try writeProfile(value, name) }
        catch { status = "本地资料暂时无法保存。" }
    }
    private func writeProfile<T: Encodable>(_ value: T, _ name: String) throws {
        try FileManager.default.createDirectory(at: AppPaths.profile, withIntermediateDirectories: true)
        try JSONEncoder().encode(value).write(to: AppPaths.profile.appendingPathComponent(name), options: .atomic)
    }
}

private struct StubbornSnapshot: Codable {
    let words: [J]
    let total: Int
    let complete: Bool
}
