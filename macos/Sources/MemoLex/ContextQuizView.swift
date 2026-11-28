import AppKit
import SwiftUI

struct ContextQuizView: View {
    @ObservedObject var model: AppModel
    let persistsProgress: Bool
    var onRebuild: ([String]) async -> [J]
    var onRebuildFreshAll: (() async -> [J])?
    var onExit: () -> Void

    @State private var questions: [J]
    @State private var currentIndex = 0
    @State private var correctCount = 0
    @State private var wrongCount = 0
    @State private var activePersist = false
    @State private var selectedLabel: String? = nil
    @State private var showHintTranslation = false
    @State private var inSummary = false
    @State private var rebuilding = false
    @State private var wrongRecords: [(question: J, chosen: J)] = []
    @State private var mistakeKinds: [String: Int] = [:]
    @State private var mistakeFeedback = ""

    init(
        model: AppModel,
        initialQuestions: [J],
        savedProgress: J? = nil,
        persistsProgress: Bool = false,
        onRebuild: @escaping ([String]) async -> [J],
        onRebuildFreshAll: (() async -> [J])? = nil,
        onExit: @escaping () -> Void
    ) {
        self.model = model
        self.persistsProgress = persistsProgress
        self.onRebuild = onRebuild
        self.onRebuildFreshAll = onRebuildFreshAll
        self.onExit = onExit

        let savedQuestions = savedProgress?["questions"].list ?? []
        let savedIdx = savedProgress?["currentIndex"].integer ?? 0
        let resume = persistsProgress && !savedQuestions.isEmpty && savedIdx < savedQuestions.count
        let activeQuestions = resume ? savedQuestions : initialQuestions
        self._questions = State(initialValue: activeQuestions)
        self._currentIndex = State(initialValue: resume ? max(0, min(savedIdx, activeQuestions.count - 1)) : 0)
        self._correctCount = State(initialValue: resume ? max(0, savedProgress?["correctCount"].integer ?? 0) : 0)
        self._wrongCount = State(initialValue: resume ? max(0, savedProgress?["wrongCount"].integer ?? 0) : 0)
        self._activePersist = State(initialValue: persistsProgress)

        var restoredWrong: [(question: J, chosen: J)] = []
        var restoredKinds: [String: Int] = [:]
        if resume, let saved = savedProgress {
            var byId: [String: J] = [:]
            for q in activeQuestions {
                let id = q["itemId"].text.lowercased()
                if !id.isEmpty { byId[id] = q }
            }
            for rec in saved["wrongRecords"].list {
                let id = rec["itemId"].text.lowercased()
                let label = rec["chosenLabel"].text
                if let q = byId[id], let opt = q["options"].list.first(where: { $0["label"].text.caseInsensitiveCompare(label) == .orderedSame }) {
                    restoredWrong.append((question: q, chosen: opt))
                }
            }
            if case .object(let dict) = saved["mistakeKinds"] {
                for (k, v) in dict where v.integer > 0 {
                    restoredKinds[k] = v.integer
                }
            }
        }
        self._wrongRecords = State(initialValue: restoredWrong)
        self._mistakeKinds = State(initialValue: restoredKinds)
    }

    private var canReset: Bool {
        currentIndex > 0 || selectedLabel != nil || inSummary
    }

    private var currentQuestion: J? {
        questions.indices.contains(currentIndex) ? questions[currentIndex] : nil
    }

    private var selectedOption: J? {
        guard let label = selectedLabel, let q = currentQuestion else { return nil }
        return q["options"].list.first { $0["label"].text == label }
    }

    private var progressRatio: Double {
        guard !questions.isEmpty else { return 0 }
        let completed = inSummary ? questions.count : (selectedLabel != nil ? currentIndex + 1 : currentIndex)
        return min(1, max(0, Double(completed) / Double(questions.count)))
    }

    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "例句填空 · 四选一自测") { model.sheet = nil }

            VStack(alignment: .leading, spacing: 12) {
                VStack(alignment: .leading, spacing: 7) {
                    HStack(spacing: 8) {
                        Text(inSummary ? "本轮例句自测完成" : "第 \(currentIndex + 1) / \(questions.count) 题 · 例句语境填空")
                            .font(.system(size: 15, weight: .bold))
                        Spacer()
                        Text(inSummary
                             ? "答对 \(correctCount) / \(questions.count) · 正确率 \(accuracyPercent)%"
                             : "答对 \(correctCount) · 答错 \(wrongCount)")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundStyle(Palette.muted)
                        Button("重置进度") {
                            resetProgress()
                        }
                        .buttonStyle(MemoButtonStyle(compact: true))
                        .disabled(!canReset || rebuilding)
                        .help("清除本地保存的例句自测进度，重新随机出题并从第 1 题开始")
                    }
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            Capsule().fill(Palette.wash)
                            Capsule().fill(Palette.accent)
                                .frame(width: max(0, geo.size.width * CGFloat(progressRatio)))
                        }
                    }
                    .frame(height: 5)
                }

                ScrollView {
                    if inSummary {
                        summaryContent
                    } else if let q = currentQuestion {
                        questionContent(q)
                    }
                }

                if !inSummary {
                    HStack {
                        let note = activePersist ? "（进度自动保存，可随时重置）" : "（当前为专项自测，不覆盖全部进度）"
                        Text(selectedLabel == nil
                             ? "快捷键：按 1–4 或 A–D 选择答案 \(note)"
                             : "按回车 / 空格进入下一题，点击解析中的单词可直接查词 \(note)")
                            .font(.system(size: 11.5))
                            .foregroundStyle(Palette.muted)
                        Spacer()
                        Button(currentIndex + 1 >= questions.count ? "查看成绩单 →" : "下一题 →") {
                            goNext()
                        }
                        .buttonStyle(MemoButtonStyle(accent: true))
                        .disabled(selectedLabel == nil)
                        .keyboardShortcut(.return, modifiers: [])
                    }
                }
            }
            .padding(22)
        }
        .overlay {
            if !inSummary {
                shortcutLayer
            }
        }
    }

    @ViewBuilder private var shortcutLayer: some View {
        Group {
            if selectedLabel == nil, let q = currentQuestion {
                let opts = q["options"].list
                ZStack {
                    if opts.indices.contains(0) {
                        Button("") { select(opts[0]) }.keyboardShortcut("1", modifiers: [])
                        Button("") { select(opts[0]) }.keyboardShortcut("a", modifiers: [])
                    }
                    if opts.indices.contains(1) {
                        Button("") { select(opts[1]) }.keyboardShortcut("2", modifiers: [])
                        Button("") { select(opts[1]) }.keyboardShortcut("b", modifiers: [])
                    }
                    if opts.indices.contains(2) {
                        Button("") { select(opts[2]) }.keyboardShortcut("3", modifiers: [])
                        Button("") { select(opts[2]) }.keyboardShortcut("c", modifiers: [])
                    }
                    if opts.indices.contains(3) {
                        Button("") { select(opts[3]) }.keyboardShortcut("4", modifiers: [])
                        Button("") { select(opts[3]) }.keyboardShortcut("d", modifiers: [])
                    }
                }
            } else if selectedLabel != nil {
                Button("") { goNext() }.keyboardShortcut(.space, modifiers: [])
            }
        }
        .opacity(0)
        .frame(width: 0, height: 0)
        .allowsHitTesting(false)
    }

    private func questionContent(_ q: J) -> some View {
        let answered = selectedLabel != nil
        let translation = q["translation"].text
        let opts = q["options"].list

        return VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Text(q["source"].text)
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 9).padding(.vertical, 3)
                        .background(Palette.wash, in: Capsule())
                        .overlay(Capsule().strokeBorder(Palette.accent.opacity(0.28), lineWidth: 0.75))
                    Spacer()
                    if !answered && !translation.trimmed.isEmpty {
                        Button(showHintTranslation ? "隐藏译文提示" : "看译文提示") {
                            showHintTranslation.toggle()
                        }
                        .buttonStyle(PillButtonStyle())
                    }
                    if answered {
                        Button("朗读完整原句") {
                            model.speakSentence(q["fullSentence"].text)
                        }
                        .buttonStyle(PillButtonStyle())
                    }
                }

                styledSentence(q, answered: answered)

                if !translation.trimmed.isEmpty && (answered || showHintTranslation) {
                    Text(translation)
                        .font(.system(size: 13.5))
                        .foregroundStyle(Palette.muted)
                        .lineSpacing(3)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.9))

            LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
                ForEach(Array(opts.enumerated()), id: \.offset) { _, opt in
                    optionButton(opt, answered: answered)
                }
            }

            if answered, let chosen = selectedOption {
                explanationCard(q, chosen: chosen)
            }
        }
        .padding(.trailing, 4)
    }

    private func styledSentence(_ q: J, answered: Bool) -> some View {
        let blanked = q["blankedSentence"].text
        let answer = q["answerDisplay"].text
        let parts = blanked.components(separatedBy: "______")
        var text = Text("")
        for i in parts.indices {
            if !parts[i].isEmpty {
                text = text + Text(parts[i]).foregroundColor(Palette.ink)
            }
            if i < parts.count - 1 {
                if answered {
                    text = text + Text(" \(answer) ")
                        .bold()
                        .foregroundColor(Color(red: 0.05, green: 0.56, blue: 0.36))
                } else {
                    text = text + Text("  ______  ")
                        .bold()
                        .foregroundColor(Palette.deep)
                }
            }
        }
        return text
            .font(.system(size: 17))
            .lineSpacing(5)
            .fixedSize(horizontal: false, vertical: true)
    }

    private func optionButton(_ opt: J, answered: Bool) -> some View {
        let label = opt["label"].text
        let display = opt["displayText"].text
        let isCorrect = opt["isCorrect"].flag
        let kind = opt["distractorKind"].text
        let isChosen = selectedLabel == label

        let colors: (bg: Color, border: Color, fg: Color) = {
            if !answered {
                return (Palette.paper, Palette.border, Palette.ink)
            }
            if isCorrect {
                return kindPalette("正确答案")
            }
            if isChosen {
                return kindPalette("你的选择")
            }
            return (Palette.paper, Palette.border, Palette.muted)
        }()

        return Button {
            select(opt)
        } label: {
            HStack(spacing: 10) {
                Text(label)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(answered && (isCorrect || isChosen) ? colors.fg : Palette.deep)
                    .frame(width: 26, height: 26)
                    .background(answered && (isCorrect || isChosen) ? colors.border.opacity(0.25) : Palette.wash, in: Circle())

                Text(display)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(Palette.ink)
                    .lineLimit(1)

                Spacer(minLength: 4)

                if answered {
                    if isCorrect {
                        kindPill("✓ 正确答案", key: "正确答案")
                    } else if isChosen {
                        kindPill("✗ \(kind)", key: "你的选择")
                    } else {
                        kindPill(kind, key: kind)
                    }
                }
            }
            .padding(.horizontal, 13).padding(.vertical, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(colors.bg, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(colors.border, lineWidth: answered && (isCorrect || isChosen) ? 1.5 : 1))
            .opacity(answered && !isCorrect && !isChosen ? 0.72 : 1)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(answered)
    }

    private func explanationCard(_ q: J, chosen: J) -> some View {
        let isRight = chosen["isCorrect"].flag
        let correctOpt = q["options"].list.first { $0["isCorrect"].flag }
        let correctLabel = correctOpt?["label"].text ?? ""
        let answerDisplay = q["answerDisplay"].text
        let verdictKey = isRight ? "正确答案" : "你的选择"
        let vColors = kindPalette(verdictKey)
        let mnemonic = q["mnemonicHint"].text

        return VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 5) {
                Text(isRight
                     ? "✓ 回答正确！本题答案：\(answerDisplay)"
                     : "✗ 选错了（你选了 \(chosen["label"].text). \(chosen["displayText"].text)），正确答案是 \(correctLabel). \(answerDisplay)")
                    .font(.system(size: 14, weight: .bold))
                    .foregroundStyle(vColors.fg)
                Text(q["explanationSummary"].text)
                    .font(.system(size: 12.5))
                    .foregroundStyle(vColors.fg)
                    .lineSpacing(3)
                    .fixedSize(horizontal: false, vertical: true)
                if !mistakeFeedback.isEmpty {
                    Text(mistakeFeedback)
                        .font(.system(size: 11.5, weight: .semibold))
                        .foregroundStyle(vColors.fg)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.horizontal, 13).padding(.vertical, 10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(vColors.bg, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(vColors.border, lineWidth: 1))

            if !mnemonic.trimmed.isEmpty {
                HStack(alignment: .top, spacing: 8) {
                    kindPill("助记", key: "近义词")
                    Text(mnemonic)
                        .font(.system(size: 12))
                        .foregroundStyle(Palette.ink)
                        .lineSpacing(2)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(.horizontal, 10).padding(.vertical, 7)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.paper, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
            }

            Text("四项辨析解析（点击任意单词可跳转查词）")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(Palette.muted)

            VStack(alignment: .leading, spacing: 6) {
                ForEach(Array(q["options"].list.enumerated()), id: \.offset) { _, opt in
                    let isCorrect = opt["isCorrect"].flag
                    let isUserWrong = !isCorrect && opt["label"].text == chosen["label"].text
                    let rowColors: (bg: Color, border: Color, fg: Color) = isCorrect
                        ? kindPalette("正确答案")
                        : isUserWrong
                            ? kindPalette("你的选择")
                            : (Palette.paper, Palette.border, Palette.ink)
                    let baseWord = opt["baseWord"].text

                    VStack(alignment: .leading, spacing: 4) {
                        HStack(spacing: 6) {
                            Button("\(opt["label"].text). \(opt["displayText"].text)") {
                                model.sheet = nil
                                model.lookup(baseWord)
                            }
                            .buttonStyle(.plain)
                            .font(.system(size: 13.5, weight: .bold))
                            .foregroundStyle(Palette.deep)
                            .help("点击查词「\(baseWord)」")

                            let ipa = opt["phonetic"].text
                            if !ipa.isEmpty {
                                Text(ipa).font(.system(size: 11.5)).foregroundStyle(Palette.muted)
                            }
                            kindPill(opt["distractorKind"].text, key: opt["distractorKind"].text)
                            if isUserWrong {
                                kindPill("你的选择", key: "你的选择")
                            }
                            Spacer()
                        }
                        Text(opt["analysis"].text)
                            .font(.system(size: 12.5))
                            .foregroundStyle(Palette.ink)
                            .lineSpacing(2)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .padding(.horizontal, 11).padding(.vertical, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(rowColors.bg, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(rowColors.border, lineWidth: 0.9))
                }
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.9))
    }

    private var accuracyPercent: Int {
        guard !questions.isEmpty else { return 0 }
        return Int(round(100.0 * Double(correctCount) / Double(questions.count)))
    }

    private var summaryContent: some View {
        let sortedMistakes = mistakeKinds.sorted { $0.value > $1.value }

        return VStack(alignment: .leading, spacing: 14) {
            VStack(spacing: 10) {
                Text("\(accuracyPercent)%")
                    .font(.system(size: 38, weight: .bold))
                    .foregroundStyle(accuracyPercent >= 80
                                     ? Color(red: 0.06, green: 0.65, blue: 0.43)
                                     : accuracyPercent >= 60 ? Palette.deep : Color(red: 0.85, green: 0.47, blue: 0.03))
                Text("共完成 \(questions.count) 道四选一语境填空 · 答对 \(correctCount) 题 · 答错 \(wrongCount) 题")
                    .font(.system(size: 13))

                if !sortedMistakes.isEmpty {
                    MorphemeFlowLayout(spacing: 6, lineSpacing: 6, expandLast: false) {
                        ForEach(sortedMistakes, id: \.key) { item in
                            kindPill("\(item.key)误选 \(item.value) 次", key: item.key)
                        }
                    }
                }

                HStack(spacing: 8) {
                    if !wrongRecords.isEmpty {
                        Button(rebuilding ? "正在出题…" : "重练错题 (\(wrongRecords.count))") {
                            Task {
                                rebuilding = true
                                let ids = wrongRecords.map { $0.question["itemId"].text }
                                let next = await onRebuild(ids)
                                rebuilding = false
                                restart(with: next, keepPersist: false)
                            }
                        }
                        .buttonStyle(MemoButtonStyle(accent: true))
                        .disabled(rebuilding)
                    }
                    Button(rebuilding ? "正在出题…" : "再来一轮") {
                        Task {
                            rebuilding = true
                            let next: [J]
                            if persistsProgress, let rebuildAll = onRebuildFreshAll {
                                next = await rebuildAll()
                            } else {
                                let ids = questions.map { $0["itemId"].text }
                                next = await onRebuild(ids)
                            }
                            rebuilding = false
                            restart(with: next, keepPersist: persistsProgress)
                        }
                    }
                    .buttonStyle(PillButtonStyle(compact: false))
                    .disabled(rebuilding)

                    Button("返回例句列表") { onExit() }
                        .buttonStyle(MemoButtonStyle(compact: true))
                }
                .padding(.top, 6)
            }
            .padding(22)
            .frame(maxWidth: .infinity)
            .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.9))

            if !wrongRecords.isEmpty {
                Text("本轮错题回顾（\(wrongRecords.count) 题）")
                    .font(.system(size: 13.5, weight: .semibold))

                ForEach(Array(wrongRecords.enumerated()), id: \.offset) { _, pair in
                    let q = pair.question
                    let chosen = pair.chosen
                    let word = q["answerBaseWord"].text
                    VStack(alignment: .leading, spacing: 6) {
                        HStack(spacing: 6) {
                            Button(word) {
                                model.sheet = nil
                                model.lookup(word)
                            }
                            .buttonStyle(PillButtonStyle())
                            .help("点击查词「\(word)」")

                            kindPill("正解：\(q["answerDisplay"].text)", key: "正确答案")
                            kindPill("误选：\(chosen["displayText"].text)（\(chosen["distractorKind"].text)）", key: "你的选择")
                            Spacer()
                        }
                        Text(q["fullSentence"].text)
                            .font(.system(size: 13.5))
                            .fixedSize(horizontal: false, vertical: true)
                        let zh = q["translation"].text
                        if !zh.trimmed.isEmpty {
                            Text(zh)
                                .font(.system(size: 12))
                                .foregroundStyle(Palette.muted)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    .padding(13)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85))
                }
            }
        }
        .padding(.trailing, 4)
    }

    private func select(_ opt: J) {
        guard !inSummary, selectedLabel == nil, let q = currentQuestion else { return }
        selectedLabel = opt["label"].text
        mistakeFeedback = ""
        let itemId = q["itemId"].text
        if !itemId.isEmpty {
            Task { await model.recordContextReview(id: itemId) }
        }
        let answerWord = q["answerBaseWord"].text
        let fullSentence = q["fullSentence"].text
        let translation = q["translation"].text
        let targetIndex = currentIndex
        if opt["isCorrect"].flag {
            correctCount += 1
            if !answerWord.isEmpty {
                Task {
                    if let res = await model.recordMistakeRight(word: answerWord),
                       res["wasInBook"].flag, targetIndex == currentIndex {
                        mistakeFeedback = res["removed"].flag
                            ? "已熟练掌握，自动移出错题本 ✓"
                            : "错题已巩固一次，继续加油！"
                    }
                }
            }
        } else {
            wrongCount += 1
            wrongRecords.append((question: q, chosen: opt))
            let kind = opt["distractorKind"].text
            mistakeKinds[kind, default: 0] += 1
            let wrongDisplay = opt["displayText"].text
            if !answerWord.isEmpty {
                Task {
                    if await model.recordMistakeWrong(
                        word: answerWord,
                        reason: "例句填空",
                        wrongInput: wrongDisplay,
                        contextSentence: fullSentence,
                        contextTranslation: translation
                    ) != nil, targetIndex == currentIndex {
                        mistakeFeedback = "已记入错题本，后续抽测将优先巩固"
                    }
                }
            }
        }
        persistProgress(nextIndex: currentIndex + 1)
    }

    private func persistProgress(nextIndex: Int) {
        guard activePersist else { return }
        let qs = questions
        let cc = correctCount
        let wc = wrongCount
        let wr = wrongRecords
        let mk = mistakeKinds
        Task {
            await model.saveContextQuizProgress(
                questions: qs,
                currentIndex: nextIndex,
                correctCount: cc,
                wrongCount: wc,
                wrongRecords: wr,
                mistakeKinds: mk
            )
        }
    }

    private func resetProgress() {
        guard !rebuilding else { return }
        guard confirm("重置例句自测进度？", "将清除本地保存的例句自测进度，重新随机出题并从第 1 题开始。", action: "重置") else {
            return
        }
        Task {
            rebuilding = true
            if persistsProgress {
                await model.clearContextQuizProgress()
            }
            let next: [J]
            if persistsProgress, let rebuildAll = onRebuildFreshAll {
                next = await rebuildAll()
            } else {
                let ids = questions.map { $0["itemId"].text }
                next = await onRebuild(ids)
            }
            rebuilding = false
            restart(with: next, keepPersist: persistsProgress)
        }
    }

    private func goNext() {
        guard !inSummary, selectedLabel != nil else { return }
        if currentIndex + 1 >= questions.count {
            inSummary = true
            if activePersist {
                Task { await model.clearContextQuizProgress() }
            }
            return
        }
        currentIndex += 1
        selectedLabel = nil
        showHintTranslation = false
        mistakeFeedback = ""
    }

    private func restart(with next: [J], keepPersist: Bool) {
        guard !next.isEmpty else { onExit(); return }
        activePersist = keepPersist
        questions = next
        currentIndex = 0
        correctCount = 0
        wrongCount = 0
        selectedLabel = nil
        showHintTranslation = false
        mistakeFeedback = ""
        inSummary = false
        wrongRecords = []
        mistakeKinds = [:]
        if activePersist {
            Task { await model.clearContextQuizProgress() }
        }
    }

    private func kindPill(_ text: String, key: String) -> some View {
        let colors = kindPalette(key)
        return Text(text)
            .font(.system(size: 10.5, weight: .semibold))
            .foregroundStyle(colors.fg)
            .padding(.horizontal, 7).padding(.vertical, 2)
            .background(colors.bg, in: Capsule())
            .overlay(Capsule().strokeBorder(colors.border, lineWidth: 0.8))
    }

    private func kindPalette(_ key: String) -> (bg: Color, border: Color, fg: Color) {
        switch key {
        case "正确答案":
            return (
                Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.14),
                Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.45),
                Color(red: 0.05, green: 0.56, blue: 0.36)
            )
        case "形近词":
            return (
                Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.14),
                Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.42),
                Color(red: 0.70, green: 0.38, blue: 0.03)
            )
        case "派生词":
            return (
                Color(red: 0.49, green: 0.36, blue: 0.90).opacity(0.14),
                Color(red: 0.49, green: 0.36, blue: 0.90).opacity(0.42),
                Color(red: 0.40, green: 0.27, blue: 0.78)
            )
        case "近义词":
            return (Palette.wash, Palette.accent.opacity(0.42), Palette.deep)
        case "你的选择":
            return (
                Color(red: 0.90, green: 0.22, blue: 0.32).opacity(0.13),
                Color(red: 0.90, green: 0.22, blue: 0.32).opacity(0.45),
                Color(red: 0.76, green: 0.14, blue: 0.24)
            )
        default:
            return (Palette.paper, Palette.border, Palette.muted)
        }
    }
}
