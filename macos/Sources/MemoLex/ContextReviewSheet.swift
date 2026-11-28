import AppKit
import SwiftUI

struct ContextReviewSheet: View {
    private enum FilterKind: String, CaseIterable {
        case all, cloud, local, unreviewed
    }

    @ObservedObject var model: AppModel
    @State private var filterText = ""
    @State private var activeFilter: FilterKind = .all
    @State private var blankMode = false
    @State private var revealedIDs: Set<String> = []
    @State private var practiceDeck: [J]? = nil
    @State private var showEditor = false
    @State private var editorItem: J? = nil

    private var allItems: [J] { model.contextSentences }

    private var cloudCount: Int {
        allItems.filter { isCloud($0) }.count
    }

    private var localCount: Int {
        allItems.count - cloudCount
    }

    private var unreviewedCount: Int {
        allItems.filter { $0["reviewCount"].integer == 0 }.count
    }

    private var distinctWordCount: Int {
        Set(allItems.map { $0["word"].text.trimmed.lowercased() }.filter { !$0.isEmpty }).count
    }

    private var filteredItems: [J] {
        let query = filterText.trimmed
        return allItems.filter { item in
            switch activeFilter {
            case .all: break
            case .cloud: if !isCloud(item) { return false }
            case .local: if isCloud(item) { return false }
            case .unreviewed: if item["reviewCount"].integer != 0 { return false }
            }
            guard !query.isEmpty else { return true }
            if item["word"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["sentence"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["translation"].text.localizedCaseInsensitiveContains(query) { return true }
            if item["source"].text.localizedCaseInsensitiveContains(query) { return true }
            return item["tags"].list.contains { $0.text.localizedCaseInsensitiveContains(query) }
        }
    }

    private var isFiltered: Bool {
        activeFilter != .all || !filterText.trimmed.isEmpty
    }

    var body: some View {
        VStack(spacing: 0) {
            if let deck = practiceDeck {
                DialogHeader(title: "我的例句 · 例句自测") { model.sheet = nil }
                let words = uniqueWords(from: deck)
                let map = contextMap(from: deck)
                StubbornFlashcardView(
                    core: model.core,
                    words: words,
                    title: "我的例句 · 例句自测",
                    savedProgress: nil,
                    persistsProgress: false,
                    preferences: model.preferences,
                    onSpeak: { model.speakWord($0, accent: $1) },
                    contextItemsByWord: map,
                    contextMode: true,
                    onContextReviewed: { id in Task { await model.recordContextReview(id: id) } }
                ) {
                    practiceDeck = nil
                }
                .padding(23)
            } else if showEditor {
                ContextSentenceEditorForm(
                    model: model,
                    editingItem: editorItem,
                    initialWord: editorItem.flatMap { let w = $0["word"].text.trimmed; return w.isEmpty ? nil : w } ?? model.word,
                    allowEditWord: editorItem == nil,
                    onClose: {
                        showEditor = false
                        editorItem = nil
                    }
                )
            } else {
                DialogHeader(title: "我的例句") { model.sheet = nil }
                listContent
            }
        }
        .frame(width: 780, height: min(680, (NSScreen.main?.visibleFrame.height ?? 780) - 60))
        .background(Palette.paper)
        .foregroundStyle(Palette.ink)
        .font(.system(size: 13))
        .toggleStyle(.checkbox)
        .noFocusRing()
        .task { await model.refreshContextSentences() }
    }

    private var listContent: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .center, spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("我的例句").font(.system(size: 22, weight: .semibold))
                    Text(summaryText).font(.system(size: 12)).foregroundStyle(Palette.muted)
                }
                Spacer()
                Button("＋ 添加例句") {
                    editorItem = nil
                    showEditor = true
                }
                .buttonStyle(PillButtonStyle(compact: false))
                .help("新建自定义例句，支持保存到本地或同步到墨墨云端")
                Button(reviewButtonTitle) {
                    guard !filteredItems.isEmpty else { return }
                    practiceDeck = filteredItems
                }
                .buttonStyle(MemoButtonStyle(accent: true))
                .disabled(filteredItems.isEmpty)
                .help("使用当前列表中的例句进行翻卡与拼写自测")
            }

            VStack(alignment: .leading, spacing: 9) {
                HStack(spacing: 12) {
                    HStack(spacing: 8) {
                        Image(systemName: "magnifyingglass").font(.system(size: 12.5)).foregroundStyle(Palette.muted)
                        TextField("筛选单词、例句、译文、来源或标签…", text: $filterText).textFieldStyle(.plain)
                    }
                    .padding(.horizontal, 10)
                    .frame(height: 33)
                    .controlSurface()

                    Toggle("挖空自测模式", isOn: $blankMode)
                        .font(.system(size: 12))
                        .onChange(of: blankMode) { _ in revealedIDs.removeAll() }
                        .help("开启后在列表中自动挖空目标词并隐藏译文，点击任意例句可揭晓答案")
                }

                HStack(spacing: 6) {
                    filterChip(.all, title: "全部 (\(allItems.count))")
                    filterChip(.cloud, title: "云端 (\(cloudCount))")
                    filterChip(.local, title: "本地 (\(localCount))")
                    filterChip(.unreviewed, title: "未自测 (\(unreviewedCount))")
                    Spacer()
                }
            }
            .padding(12)
            .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))

            ScrollView {
                if filteredItems.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: "text.quote").font(.system(size: 28)).foregroundStyle(Palette.muted)
                        Text(allItems.isEmpty ? "还没有收藏或自建例句" : "没有符合当前筛选条件的例句")
                            .font(.system(size: 15, weight: .semibold))
                        Text(allItems.isEmpty
                             ? "点击右上角「＋ 添加例句」开始记录，或在真题例句、翻译中一键收藏。"
                             : "可以清空搜索关键词或切换筛选标签查看其他例句。")
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
                            sentenceCard(item)
                        }
                    }
                    .padding(.trailing, 4)
                }
            }

            Text("点击单词可跳转查词；开启「挖空自测模式」可在列表内直接练习，或点击右上角「开始自测」进入翻卡与拼写默写。")
                .font(.system(size: 11.5))
                .foregroundStyle(Palette.muted)
        }
        .padding(22)
    }

    private var summaryText: String {
        if allItems.isEmpty {
            return "在真题例句、段落翻译中点击「收藏例句」，或直接在此「添加例句」"
        }
        if cloudCount > 0 {
            return "共 \(allItems.count) 条例句 · \(distinctWordCount) 个单词 · 云端 \(cloudCount) 条"
        }
        return "共 \(allItems.count) 条例句 · \(distinctWordCount) 个单词"
    }

    private var reviewButtonTitle: String {
        if filteredItems.isEmpty { return "开始自测" }
        return isFiltered ? "自测当前 (\(filteredItems.count))" : "开始自测 (\(filteredItems.count))"
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

    private func sentenceCard(_ item: J) -> some View {
        let id = item["id"].text
        let word = item["word"].text
        let sentence = item["sentence"].text
        let trans = item["translation"].text
        let source = item["source"].text
        let reviews = item["reviewCount"].integer
        let matchedForm = item["matchedForm"].text
        let cloud = isCloud(item)
        let unsynced = isUnsynced(item)
        let tags = item["tags"].list.map(\.text).filter { !$0.isEmpty }
        let revealed = !blankMode || revealedIDs.contains(id)
        let sourceMeta = reviews > 0 ? "\(source) · 已自测 \(reviews) 次" : source

        return VStack(alignment: .leading, spacing: 7) {
            HStack(alignment: .center, spacing: 6) {
                Button(revealed ? word : "点击揭晓单词") {
                    if !revealed {
                        revealedIDs.insert(id)
                    } else {
                        model.sheet = nil
                        model.lookup(word)
                    }
                }
                .buttonStyle(PillButtonStyle())
                .help(revealed ? "点击跳转到「\(word)」词条页" : "点击揭晓目标词与译文")

                Text(cloud ? "云端" : unsynced ? "待同步" : "本地")
                    .font(.system(size: 10.5, weight: .semibold))
                    .foregroundStyle(cloud ? Palette.deep : unsynced ? Color(red: 0.65, green: 0.34, blue: 0.03) : Palette.muted)
                    .padding(.horizontal, 7).padding(.vertical, 2)
                    .background(cloud ? Palette.accent.opacity(0.18) : unsynced ? Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.16) : Palette.paper, in: Capsule())
                    .overlay(Capsule().strokeBorder(cloud ? Palette.accent.opacity(0.32) : unsynced ? Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.35) : Palette.border, lineWidth: 0.75))

                Text(sourceMeta)
                    .font(.system(size: 10.5))
                    .foregroundStyle(Palette.muted)
                    .padding(.horizontal, 7).padding(.vertical, 2)
                    .background(Palette.paper, in: Capsule())
                    .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.7))

                ForEach(Array(tags.prefix(3).enumerated()), id: \.offset) { _, tag in
                    Text(tag)
                        .font(.system(size: 10))
                        .foregroundStyle(Palette.muted)
                        .padding(.horizontal, 6).padding(.vertical, 1.5)
                        .background(Palette.paper, in: Capsule())
                        .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.65))
                }

                Spacer(minLength: 4)

                Button("朗读") {
                    model.speakSentence(sentence)
                }
                .buttonStyle(PillButtonStyle())
                .help("朗读例句")

                Button("编辑") {
                    editorItem = item
                    showEditor = true
                }
                .buttonStyle(.plain)
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(Palette.deep)

                Button("删除") {
                    Task { _ = try? await model.deleteContextSentence(item, rethrowOnError: false) }
                }
                .buttonStyle(.plain)
                .font(.system(size: 11))
                .foregroundStyle(Palette.muted)
            }

            if revealed {
                LinkedText(text: sentence, highlights: [word, matchedForm].filter { !$0.isEmpty }, size: 14, color: Palette.ink)
                if !trans.isEmpty {
                    Text(trans).font(.system(size: 12.5)).foregroundStyle(Palette.muted).lineSpacing(3)
                }
            } else {
                Text(Self.blankOutWord(sentence: sentence, word: word, matchedForm: matchedForm))
                    .font(.system(size: 14))
                    .foregroundStyle(Palette.ink)
                    .lineSpacing(3)
                    .fixedSize(horizontal: false, vertical: true)
                Text(trans.isEmpty ? "点击卡片揭晓目标词" : "点击卡片揭晓目标词与中文译文")
                    .font(.system(size: 11.5))
                    .foregroundStyle(Palette.muted.opacity(0.85))
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 11)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.wash.opacity(0.68), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85))
        .contentShape(Rectangle())
        .onTapGesture {
            guard blankMode else { return }
            if revealedIDs.contains(id) { revealedIDs.remove(id) }
            else { revealedIDs.insert(id) }
        }
    }

    private func isCloud(_ item: J) -> Bool {
        item["origin"].text == "cloud" && !item["cloudId"].text.isEmpty
    }

    private func isUnsynced(_ item: J) -> Bool {
        item["origin"].text != "cloud" && !item["cloudId"].text.isEmpty
    }

    private func uniqueWords(from items: [J]) -> [String] {
        var seen = Set<String>()
        var result: [String] = []
        for item in items {
            let w = item["word"].text.trimmed
            if !w.isEmpty, seen.insert(w.lowercased()).inserted {
                result.append(w)
            }
        }
        return result
    }

    private func contextMap(from items: [J]) -> [String: J] {
        var map: [String: J] = [:]
        for item in items {
            let key = item["word"].text.trimmed.lowercased()
            if !key.isEmpty, map[key] == nil {
                map[key] = item
            }
        }
        return map
    }

    private static func blankOutWord(sentence: String, word: String, matchedForm: String) -> String {
        let targets = [matchedForm.trimmed, word.trimmed].filter { !$0.isEmpty }
        guard !targets.isEmpty else { return sentence }
        var output = sentence
        var replaced = false
        for target in targets {
            let escaped = NSRegularExpression.escapedPattern(for: target)
            if let regex = try? NSRegularExpression(pattern: "\\b\(escaped)\\b", options: [.caseInsensitive]) {
                let range = NSRange(output.startIndex..., in: output)
                if regex.firstMatch(in: output, range: range) != nil {
                    output = regex.stringByReplacingMatches(in: output, range: range, withTemplate: "______")
                    replaced = true
                }
            }
        }
        if !replaced {
            var stem = word.trimmed.lowercased()
            if stem.count > 4 && stem.hasSuffix("e") { stem.removeLast() }
            if stem.count >= 4 {
                let escaped = NSRegularExpression.escapedPattern(for: stem)
                if let regex = try? NSRegularExpression(pattern: "\\b\(escaped)[a-zA-Z]*\\b", options: [.caseInsensitive]) {
                    let range = NSRange(output.startIndex..., in: output)
                    output = regex.stringByReplacingMatches(in: output, range: range, withTemplate: "______")
                }
            }
        }
        return output
    }
}
