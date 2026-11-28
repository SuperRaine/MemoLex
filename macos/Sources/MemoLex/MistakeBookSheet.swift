import AppKit
import SwiftUI

struct MistakeBookSheet: View {
    private enum BookTab: String {
        case spelling
        case sentence
    }

    private enum SubFilter: String {
        case all
        case highWeight
    }

    private enum ActivePractice {
        case spelling([String])
        case sentence(questions: [J], filterWords: [String]?)
    }

    @ObservedObject var model: AppModel
    @State private var activeTab: BookTab = .spelling
    @State private var activeSubFilter: SubFilter = .all
    @State private var filterText = ""
    @State private var activePractice: ActivePractice? = nil
    @State private var buildingQuiz = false
    @State private var addingToCloud = false
    @State private var noticeText = ""
    @State private var initializedTab = false

    private var spellingItems: [J] {
        model.mistakeItems.filter { item in
            let cat = item["category"].text.lowercased()
            if !cat.isEmpty { return cat == "spelling" }
            return item["lastReason"].text.contains("拼写")
        }
    }

    private var sentenceItems: [J] {
        model.mistakeItems.filter { item in
            let cat = item["category"].text.lowercased()
            if !cat.isEmpty { return cat == "sentence" }
            return item["lastReason"].text.contains("例句")
        }
    }

    private var currentBookItems: [J] {
        activeTab == .sentence ? sentenceItems : spellingItems
    }

    private var highWeightCount: Int {
        currentBookItems.filter { $0["weight"].integer >= 6 }.count
    }

    private var filteredItems: [J] {
        let query = filterText.trimmed
        return currentBookItems.filter { item in
            if activeSubFilter == .highWeight && item["weight"].integer < 6 { return false }
            guard !query.isEmpty else { return true }
            if item["word"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["meaning"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["lastWrongInput"].text.localizedCaseInsensitiveContains(query) { return true }
            return item["contextSentence"].text.localizedCaseInsensitiveContains(query)
        }
    }

    private var isFiltered: Bool {
        activeSubFilter != .all || !filterText.trimmed.isEmpty
    }

    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: activeHeaderTitle) { model.sheet = nil }
            switch activePractice {
            case .spelling(let words):
                StubbornFlashcardView(
                    core: model.core,
                    words: words,
                    title: "拼写错题 · 默写抽测",
                    persistsProgress: false,
                    preferences: model.preferences,
                    onSpeak: { model.speakWord($0, accent: $1) },
                    contextItemsByWord: model.contextSentenceMap,
                    onContextReviewed: { id in Task { await model.recordContextReview(id: id) } },
                    startInSpellingMode: true,
                    onMistakeChanged: { Task { await model.refreshMistakeBook() } }
                ) {
                    activePractice = nil
                    Task {
                        await model.markMistakeBookViewed()
                        await model.refreshMistakeBook()
                    }
                }
                .padding(22)
            case .sentence(let questions, let filterWords):
                ContextQuizView(
                    model: model,
                    initialQuestions: questions,
                    persistsProgress: false,
                    onRebuild: { retryIds in
                        let retrySet = Set(retryIds.map { $0.lowercased() })
                        let retryWords = questions
                            .filter { retrySet.contains($0["itemId"].text.lowercased()) }
                            .map { $0["answerBaseWord"].text }
                            .filter { !$0.isEmpty }
                        return await model.buildSentenceMistakeQuiz(words: retryWords.isEmpty ? filterWords : retryWords)
                    },
                    onRebuildFreshAll: {
                        await model.buildSentenceMistakeQuiz(words: filterWords)
                    }
                ) {
                    activePractice = nil
                    Task {
                        await model.markMistakeBookViewed()
                        await model.refreshMistakeBook()
                    }
                }
                .padding(22)
            case nil:
                listContent
            }
        }
        .frame(width: 780, height: min(690, (NSScreen.main?.visibleFrame.height ?? 780) - 60))
        .background(Palette.paper)
        .foregroundStyle(Palette.ink)
        .font(.system(size: 13))
        .noFocusRing()
        .task {
            await model.markMistakeBookViewed()
            await model.refreshMistakeBook()
            if !initializedTab {
                initializedTab = true
                if spellingItems.isEmpty && !sentenceItems.isEmpty {
                    activeTab = .sentence
                }
            }
        }
    }

    private var activeHeaderTitle: String {
        switch activePractice {
        case .spelling: return "拼写错题 · 默写抽测"
        case .sentence: return "例句错题 · 四选一抽测"
        case nil: return "错题本"
        }
    }

    private var listContent: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .center, spacing: 8) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("错题本").font(.system(size: 22, weight: .semibold))
                    Text(summaryText).font(.system(size: 12)).foregroundStyle(Palette.muted)
                }
                Spacer()
                if model.loggedIn && !model.selectedNotepadTitle.isEmpty {
                    Button(addingToCloud ? "加入中…" : "加入云词本") {
                        Task { await addFilteredToCloud() }
                    }
                    .buttonStyle(PillButtonStyle(compact: false))
                    .disabled(filteredItems.isEmpty || addingToCloud)
                    .help("将当前列表中的错词批量加入到「\(model.selectedNotepadTitle)」")
                }

                Button(primaryPracticeTitle) {
                    Task { await startCurrentBookPractice() }
                }
                .buttonStyle(MemoButtonStyle(accent: true))
                .disabled(filteredItems.isEmpty || buildingQuiz)
                .help(activeTab == .sentence
                      ? "以四选一语境选择题形式抽测这些例句错题，答对自动巩固并逐步移出"
                      : "直接进入拼写默写模式随机抽测这些拼写错题，常错词优先出现")
            }

            HStack(spacing: 4) {
                bookTabButton(.spelling, title: "拼写错题 (\(spellingItems.count))")
                bookTabButton(.sentence, title: "例句错题 (\(sentenceItems.count))")
                Spacer()
            }

            HStack(spacing: 8) {
                Image(systemName: "info.circle.fill")
                    .font(.system(size: 12.5))
                    .foregroundStyle(Palette.deep)
                Text(activeTab == .sentence
                     ? "例句四选一自测中答错的题目会自动收录到这里；点击「例句选择题抽测」继续刷选择题，多次答对后自动移出。"
                     : "拼写默写中拼错的单词会自动收录到这里；点击「拼写抽测」集中默写，常错词优先出现，多次拼对后自动移出。")
                    .font(.system(size: 11.5))
                    .foregroundStyle(Palette.deep)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 12).padding(.vertical, 8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Palette.wash.opacity(0.82), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.accent.opacity(0.28), lineWidth: 0.8))

            if !noticeText.isEmpty {
                Text(noticeText)
                    .font(.system(size: 11.5, weight: .medium))
                    .foregroundStyle(Color(red: 0.05, green: 0.54, blue: 0.36))
            }

            HStack(spacing: 8) {
                HStack(spacing: 8) {
                    Image(systemName: "magnifyingglass").font(.system(size: 12.5)).foregroundStyle(Palette.muted)
                    TextField("搜索当前分类中的单词、释义或例句…", text: $filterText).textFieldStyle(.plain)
                }
                .padding(.horizontal, 10)
                .frame(height: 33)
                .controlSurface()

                subFilterChip(.all, title: "全部 (\(currentBookItems.count))")
                subFilterChip(.highWeight, title: "重点易错 (\(highWeightCount))")
            }
            .padding(10)
            .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))

            ScrollView {
                if filteredItems.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: currentBookItems.isEmpty ? "checkmark.seal" : "line.3.horizontal.decrease.circle")
                            .font(.system(size: 28))
                            .foregroundStyle(Palette.muted)
                        Text(currentBookItems.isEmpty
                             ? (activeTab == .sentence ? "暂无例句错题" : "暂无拼写错题")
                             : "没有符合当前筛选条件的错题")
                            .font(.system(size: 15, weight: .semibold))
                        Text(currentBookItems.isEmpty
                             ? (activeTab == .sentence
                                ? "在「例句四选一自测」中选错的题目会自动收录到这里，方便继续刷选择题。"
                                : "在「拼写默写」中输入有效英文但拼错的单词会自动进入这里，方便集中抽测。")
                             : "可以清空搜索关键词或切换「全部」查看该分类下的其他错题。")
                            .font(.system(size: 12))
                            .foregroundStyle(Palette.muted)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 44)
                    .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
                } else {
                    LazyVStack(alignment: .leading, spacing: 8) {
                        ForEach(Array(filteredItems.enumerated()), id: \.offset) { _, item in
                            mistakeCard(item)
                        }
                    }
                    .padding(.trailing, 4)
                }
            }

            Text(activeTab == .sentence
                 ? "点击单词可跳转查词；点击「例句选择题抽测」继续用四选一选择题巩固例句错题。"
                 : "点击单词可跳转查词；点击「拼写抽测」集中默写巩固拼写错题，熟练掌握后自动移出。")
                .font(.system(size: 11.5))
                .foregroundStyle(Palette.muted)
        }
        .padding(22)
    }

    private var summaryText: String {
        if spellingItems.isEmpty && sentenceItems.isEmpty {
            return "仅收录拼写默写与例句选择题的错题，多次答对后自动移出"
        }
        return "拼写错题 \(spellingItems.count) 词 · 例句错题 \(sentenceItems.count) 题"
    }

    private var primaryPracticeTitle: String {
        if activeTab == .sentence {
            if buildingQuiz { return "正在出题…" }
            return filteredItems.isEmpty ? "例句选择题抽测" : "例句选择题抽测 (\(filteredItems.count))"
        }
        return filteredItems.isEmpty ? "拼写抽测" : "拼写抽测 (\(filteredItems.count))"
    }

    private func bookTabButton(_ tab: BookTab, title: String) -> some View {
        let selected = activeTab == tab
        return Button(title) {
            if activeTab != tab {
                activeTab = tab
                activeSubFilter = .all
                noticeText = ""
            }
        }
        .font(.system(size: 12.5, weight: selected ? .semibold : .regular))
        .foregroundStyle(selected ? Palette.deep : Palette.muted)
        .padding(.horizontal, 14)
        .padding(.vertical, 6)
        .background(selected ? Palette.wash : Palette.canvas, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(selected ? Palette.accent : Palette.border, lineWidth: selected ? 1.1 : 0.85))
        .buttonStyle(.plain)
    }

    private func subFilterChip(_ kind: SubFilter, title: String) -> some View {
        let selected = activeSubFilter == kind
        return Button(title) {
            activeSubFilter = kind
        }
        .font(.system(size: 11.5, weight: selected ? .semibold : .regular))
        .foregroundStyle(selected ? Palette.deep : Palette.muted)
        .padding(.horizontal, 10)
        .padding(.vertical, 5)
        .background(selected ? Palette.wash : Palette.paper, in: Capsule())
        .overlay(Capsule().strokeBorder(selected ? Palette.accent : Palette.border, lineWidth: 0.9))
        .buttonStyle(.plain)
    }

    private func weightPalette(_ weight: Int) -> (bg: Color, border: Color, fg: Color) {
        if weight >= 6 {
            return (
                Color(red: 0.90, green: 0.22, blue: 0.32).opacity(0.14),
                Color(red: 0.90, green: 0.22, blue: 0.32).opacity(0.45),
                Color(red: 0.76, green: 0.14, blue: 0.24)
            )
        }
        if weight >= 3 {
            return (
                Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.15),
                Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.44),
                Color(red: 0.70, green: 0.38, blue: 0.03)
            )
        }
        return (
            Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.14),
            Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.42),
            Color(red: 0.05, green: 0.56, blue: 0.36)
        )
    }

    private func mistakeCard(_ item: J) -> some View {
        let word = item["word"].text
        let weight = item["weight"].integer
        let wrongCount = item["wrongCount"].integer
        let rightCount = item["rightCount"].integer
        let lastWrongInput = item["lastWrongInput"].text
        let phonetic = item["phonetic"].text
        let meaning = item["meaning"].text.decodedLines.replacingOccurrences(of: "\n", with: "；")
        let sentence = item["contextSentence"].text
        let translation = item["contextTranslation"].text
        let isSentence = activeTab == .sentence
        let wColors = weightPalette(weight)
        let levelLabel = weight >= 6 ? "重点易错" : weight >= 3 ? "待巩固" : "快掌握"
        let statsLabel = rightCount > 0 ? "错 \(wrongCount) · 对 \(rightCount)" : "错 \(wrongCount) 次"

        return VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .center, spacing: 6) {
                Text(levelLabel)
                    .font(.system(size: 11, weight: .bold))
                    .foregroundStyle(wColors.fg)
                    .padding(.horizontal, 8).padding(.vertical, 3)
                    .background(wColors.bg, in: Capsule())
                    .overlay(Capsule().strokeBorder(wColors.border, lineWidth: 0.85))

                Button(word) {
                    model.sheet = nil
                    model.lookup(word)
                }
                .buttonStyle(PillButtonStyle())
                .help("点击跳转到「\(word)」词条页")

                if !phonetic.isEmpty {
                    Text(phonetic)
                        .font(.system(size: 11.5))
                        .foregroundStyle(Palette.muted)
                }

                Text(statsLabel)
                    .font(.system(size: 10.5))
                    .foregroundStyle(Palette.muted)
                    .padding(.horizontal, 7).padding(.vertical, 2)
                    .background(Palette.paper, in: Capsule())
                    .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.7))

                Spacer(minLength: 4)

                Button("朗读") {
                    model.speakWord(word, accent: .american)
                }
                .buttonStyle(PillButtonStyle())
                .help("朗读单词")

                Button("移出") {
                    Task { await model.removeMistake(word: word, category: activeTab.rawValue) }
                }
                .buttonStyle(.plain)
                .font(.system(size: 11))
                .foregroundStyle(Palette.muted)
                .help(isSentence ? "从例句错题中移出" : "从拼写错题中移出")
            }

            if !lastWrongInput.trimmed.isEmpty {
                let wrongLabel = isSentence ? "上次误选：\(lastWrongInput)" : "上次误拼：\(lastWrongInput)"
                Text(wrongLabel)
                    .font(.system(size: 11, weight: .medium))
                    .foregroundStyle(Color(red: 0.76, green: 0.14, blue: 0.24))
                    .padding(.horizontal, 8).padding(.vertical, 2.5)
                    .background(Color(red: 0.90, green: 0.22, blue: 0.32).opacity(0.11), in: Capsule())
            }

            if !meaning.trimmed.isEmpty {
                Text(meaning)
                    .font(.system(size: 13))
                    .foregroundStyle(Palette.ink)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
            }

            if isSentence && !sentence.trimmed.isEmpty {
                VStack(alignment: .leading, spacing: 2) {
                    Text(sentence)
                        .font(.system(size: 12))
                        .foregroundStyle(Palette.ink)
                        .fixedSize(horizontal: false, vertical: true)
                    if !translation.trimmed.isEmpty {
                        Text(translation)
                            .font(.system(size: 11.5))
                            .foregroundStyle(Palette.muted)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                .padding(.horizontal, 10).padding(.vertical, 7)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.paper.opacity(0.85), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.7))
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.wash.opacity(0.66), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85))
    }

    private func startCurrentBookPractice() async {
        let filterWords = filteredItems.map { $0["word"].text }.filter { !$0.isEmpty }
        guard !filterWords.isEmpty else { return }
        noticeText = ""

        if activeTab == .sentence {
            buildingQuiz = true
            defer { buildingQuiz = false }
            let targetFilter = isFiltered ? filterWords : nil
            let questions = await model.buildSentenceMistakeQuiz(words: targetFilter)
            if questions.isEmpty {
                noticeText = "这些例句错题缺少对应例句，请先在「我的例句」中补充例句。"
                return
            }
            activePractice = .sentence(questions: questions, filterWords: targetFilter)
        } else {
            let sampled = await model.sampleMistakeWords(filterWords: isFiltered ? filterWords : nil, category: "spelling")
            let finalWords = sampled.isEmpty ? filterWords.shuffled() : sampled
            activePractice = .spelling(finalWords)
        }
    }

    private func addFilteredToCloud() async {
        var seen = Set<String>()
        let words = filteredItems.map { $0["word"].text }.filter { w in
            let k = w.lowercased()
            return !k.isEmpty && seen.insert(k).inserted
        }
        guard !words.isEmpty else { return }
        addingToCloud = true
        defer { addingToCloud = false }
        do {
            let added = try await model.batchAddToNotepad(words)
            noticeText = added > 0
                ? "已将 \(added) 个错词加入云词本「\(model.selectedNotepadTitle)」。"
                : "当前筛选的错词都已在云词本「\(model.selectedNotepadTitle)」中。"
            model.status = noticeText
        } catch {
            noticeText = ""
            model.fail(error)
        }
    }
}
