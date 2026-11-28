import AppKit
import SwiftUI

struct MistakeBookSheet: View {
    private enum FilterKind: String, CaseIterable {
        case all, highWeight, spelling, quiz, flashcard
    }

    @ObservedObject var model: AppModel
    @State private var filterText = ""
    @State private var activeFilter: FilterKind = .all
    @State private var practiceSession: (words: [String], spellingMode: Bool)? = nil
    @State private var addingToCloud = false
    @State private var cloudNotice = ""

    private var allItems: [J] { model.mistakeItems }

    private var totalWeight: Int {
        allItems.reduce(0) { $0 + $1["weight"].integer }
    }

    private var highWeightCount: Int {
        allItems.filter { $0["weight"].integer >= 6 }.count
    }

    private var spellingCount: Int {
        allItems.filter { $0["lastReason"].text.contains("拼写") }.count
    }

    private var quizCount: Int {
        allItems.filter { $0["lastReason"].text.contains("例句") || $0["lastReason"].text.contains("同根") }.count
    }

    private var flashcardCount: Int {
        allItems.filter { $0["lastReason"].text.contains("卡片") }.count
    }

    private var filteredItems: [J] {
        let query = filterText.trimmed
        return allItems.filter { item in
            switch activeFilter {
            case .all: break
            case .highWeight: if item["weight"].integer < 6 { return false }
            case .spelling: if !item["lastReason"].text.contains("拼写") { return false }
            case .quiz:
                let r = item["lastReason"].text
                if !r.contains("例句") && !r.contains("同根") { return false }
            case .flashcard: if !item["lastReason"].text.contains("卡片") { return false }
            }
            guard !query.isEmpty else { return true }
            if item["word"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["meaning"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["lastWrongInput"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["lastReason"].text.localizedCaseInsensitiveContains(query) { return true }
            return item["contextSentence"].text.localizedCaseInsensitiveContains(query)
        }
    }

    private var isFiltered: Bool {
        activeFilter != .all || !filterText.trimmed.isEmpty
    }

    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "错题本") { model.sheet = nil }
            if let session = practiceSession {
                StubbornFlashcardView(
                    core: model.core,
                    words: session.words,
                    title: session.spellingMode ? "错题本 · 拼写随机抽测" : "错题本 · 卡片随机抽测",
                    persistsProgress: false,
                    preferences: model.preferences,
                    onSpeak: { model.speakWord($0, accent: $1) },
                    contextItemsByWord: model.contextSentenceMap,
                    onContextReviewed: { id in Task { await model.recordContextReview(id: id) } },
                    startInSpellingMode: session.spellingMode,
                    onMistakeChanged: { Task { await model.refreshMistakeBook() } }
                ) {
                    practiceSession = nil
                    Task { await model.refreshMistakeBook() }
                }
                .padding(22)
            } else {
                listContent
            }
        }
        .frame(width: 780, height: min(690, (NSScreen.main?.visibleFrame.height ?? 780) - 60))
        .background(Palette.paper)
        .foregroundStyle(Palette.ink)
        .font(.system(size: 13))
        .noFocusRing()
        .task { await model.refreshMistakeBook() }
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
                    .help("将当前筛选的错词批量加入到「\(model.selectedNotepadTitle)」")
                }
                Button(spellingButtonTitle) {
                    Task { await startPractice(spellingMode: true) }
                }
                .buttonStyle(PillButtonStyle(compact: false))
                .disabled(filteredItems.isEmpty)
                .help("按权重随机顺序直接进入拼写默写抽测（答对权重 -1，答错 +3）")

                Button(cardButtonTitle) {
                    Task { await startPractice(spellingMode: false) }
                }
                .buttonStyle(MemoButtonStyle(accent: true))
                .disabled(filteredItems.isEmpty)
                .help("像卡片一样按权重随机抽测错词（答对权重 -1，归 0 自动移出）")
            }

            HStack(spacing: 8) {
                Image(systemName: "info.circle.fill")
                    .font(.system(size: 12.5))
                    .foregroundStyle(Palette.deep)
                Text("权重机制：答错进入错题本并 +3 权重；答对错题 -1 权重；权重归 0 自动移出。随机抽测时高权重词优先出现。")
                    .font(.system(size: 11.5))
                    .foregroundStyle(Palette.deep)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 12).padding(.vertical, 8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Palette.wash.opacity(0.82), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.accent.opacity(0.28), lineWidth: 0.8))

            if !cloudNotice.isEmpty {
                Text(cloudNotice)
                    .font(.system(size: 11.5, weight: .medium))
                    .foregroundStyle(Color(red: 0.05, green: 0.54, blue: 0.36))
            }

            VStack(alignment: .leading, spacing: 9) {
                HStack(spacing: 8) {
                    Image(systemName: "magnifyingglass").font(.system(size: 12.5)).foregroundStyle(Palette.muted)
                    TextField("搜索错词、释义、误拼输入或来源…", text: $filterText).textFieldStyle(.plain)
                }
                .padding(.horizontal, 10)
                .frame(height: 33)
                .controlSurface()

                HStack(spacing: 6) {
                    filterChip(.all, title: "全部 (\(allItems.count))")
                    filterChip(.highWeight, title: "高权重 ≥6 (\(highWeightCount))")
                    filterChip(.spelling, title: "拼写错误 (\(spellingCount))")
                    filterChip(.quiz, title: "例句/辨析 (\(quizCount))")
                    filterChip(.flashcard, title: "卡片忘记 (\(flashcardCount))")
                    Spacer()
                }
            }
            .padding(12)
            .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))

            ScrollView {
                if filteredItems.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: allItems.isEmpty ? "checkmark.seal" : "line.3.horizontal.decrease.circle")
                            .font(.system(size: 28))
                            .foregroundStyle(Palette.muted)
                        Text(allItems.isEmpty ? "错题本目前是空的" : "没有符合当前筛选条件的错词")
                            .font(.system(size: 15, weight: .semibold))
                        Text(allItems.isEmpty
                             ? "在例句四选一自测、卡片自测、拼写默写或同根辨析中答错的单词会自动进入这里。"
                             : "可以清空搜索关键词或切换筛选标签查看其他错词。")
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

            Text("点击单词可跳转查词；点击「随机抽测」或「拼写抽测」可像卡片一样按权重随机练习，答对自动减权重直至归 0 移出。")
                .font(.system(size: 11.5))
                .foregroundStyle(Palette.muted)
        }
        .padding(22)
    }

    private var summaryText: String {
        if allItems.isEmpty {
            return "答错自动记入（权重 +3），答对逐步消化（权重 -1，归 0 移出）"
        }
        return "共 \(allItems.count) 词 · 总权重 \(totalWeight) · 高权重（≥6）\(highWeightCount) 词"
    }

    private var cardButtonTitle: String {
        if filteredItems.isEmpty { return "随机抽测" }
        return isFiltered ? "随机抽测 (\(filteredItems.count))" : "随机抽测 (\(filteredItems.count))"
    }

    private var spellingButtonTitle: String {
        if filteredItems.isEmpty { return "拼写抽测" }
        return "拼写抽测 (\(filteredItems.count))"
    }

    private func filterChip(_ kind: FilterKind, title: String) -> some View {
        let selected = activeFilter == kind
        return Button(title) {
            activeFilter = kind
        }
        .font(.system(size: 11.5, weight: selected ? .semibold : .regular))
        .foregroundStyle(selected ? Palette.deep : Palette.muted)
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
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
        let lastReason = item["lastReason"].text
        let lastWrongInput = item["lastWrongInput"].text
        let phonetic = item["phonetic"].text
        let meaning = item["meaning"].text.decodedLines.replacingOccurrences(of: "\n", with: "；")
        let sentence = item["contextSentence"].text
        let wColors = weightPalette(weight)
        let dotCount = min(5, max(1, (weight + 2) / 3))
        let statsLabel = rightCount > 0 ? "\(lastReason) · 错 \(wrongCount) · 对 \(rightCount)" : "\(lastReason) · 错 \(wrongCount) 次"

        return VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .center, spacing: 6) {
                HStack(spacing: 4) {
                    Text("权重 \(weight)")
                        .font(.system(size: 11, weight: .bold))
                        .monospacedDigit()
                    HStack(spacing: 2) {
                        ForEach(0..<dotCount, id: \.self) { _ in
                            Circle().fill(wColors.fg).frame(width: 4.5, height: 4.5)
                        }
                    }
                }
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
                    Task { await model.removeMistake(word: word) }
                }
                .buttonStyle(.plain)
                .font(.system(size: 11))
                .foregroundStyle(Palette.muted)
                .help("手动将此词移出错题本")
            }

            if !lastWrongInput.trimmed.isEmpty {
                let wrongLabel = lastReason.contains("拼写") ? "上次误拼：\(lastWrongInput)" : "上次误选：\(lastWrongInput)"
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

            if !sentence.trimmed.isEmpty {
                Text("例句：\(sentence)")
                    .font(.system(size: 11.5))
                    .foregroundStyle(Palette.muted)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.wash.opacity(0.66), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85))
    }

    private func startPractice(spellingMode: Bool) async {
        let filterWords = filteredItems.map { $0["word"].text }.filter { !$0.isEmpty }
        guard !filterWords.isEmpty else { return }
        let sampled = await model.sampleMistakeWords(filterWords: isFiltered ? filterWords : nil)
        let finalWords = sampled.isEmpty ? filterWords.shuffled() : sampled
        practiceSession = (words: finalWords, spellingMode: spellingMode)
    }

    private func addFilteredToCloud() async {
        let words = filteredItems.map { $0["word"].text }.filter { !$0.isEmpty }
        guard !words.isEmpty else { return }
        addingToCloud = true
        defer { addingToCloud = false }
        do {
            let added = try await model.batchAddToNotepad(words)
            cloudNotice = added > 0
                ? "已将 \(added) 个错词加入云词本「\(model.selectedNotepadTitle)」。"
                : "当前筛选的错词都已在云词本「\(model.selectedNotepadTitle)」中。"
            model.status = cloudNotice
        } catch {
            cloudNotice = ""
            model.fail(error)
        }
    }
}
