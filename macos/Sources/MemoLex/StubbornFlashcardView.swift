import SwiftUI

struct StubbornFlashcardView: View {
    let core: CoreBridge
    let freshWords: [String]
    let title: String
    let persistsProgress: Bool
    let preferences: Preferences?
    let onSpeak: ((String, EnglishAccent) -> Void)?
    let onSaveProgress: (FlashcardProgressSnapshot) -> Void
    let onClearProgress: () -> Void
    let contextItemsByWord: [String: J]
    let contextMode: Bool
    let onContextReviewed: ((String) -> Void)?
    let trackMistakes: Bool
    let onMistakeChanged: (() -> Void)?
    let close: () -> Void
    @State private var practice: FlashcardPractice
    @State private var revealed = false
    @State private var flipping = false
    @State private var swiping = false
    @State private var swipeOffset: CGFloat = 0
    @State private var swipeAngle: Double = 0
    @State private var swipeOpacity: Double = 1
    @State private var flipTask: Task<Void, Never>?
    @State private var autoFlipTask: Task<Void, Never>?
    @State private var lookup: (word: String, data: J)?
    @State private var spellingMode = false
    @State private var typedSpelling = ""
    @State private var spellingFeedback = ""
    @State private var spellingCorrect = false
    @State private var spellingClue = ""
    @State private var spellingClueTone = "info"
    @State private var spellingHintLevel = 0
    @State private var activeDrill: J?
    @State private var microDrillDone = false
    @State private var microChoices: [String] = []
    @State private var hadMistakeOnCurrentWord = false
    @State private var mistakeEvaluatedOnCard = false
    @State private var currentMistakeWeight: Int? = nil
    @State private var mistakeNotice = ""
    @State private var microDrillFeedback = ""
    @State private var microDrillWasCorrect = false
    @State private var autoSpeak: String
    @FocusState private var spellingFocused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(
        core: CoreBridge,
        words: [String],
        title: String = "卡片自测",
        savedProgress: FlashcardProgressSnapshot? = nil,
        persistsProgress: Bool = true,
        preferences: Preferences? = nil,
        onSpeak: ((String, EnglishAccent) -> Void)? = nil,
        onSaveProgress: @escaping (FlashcardProgressSnapshot) -> Void = { _ in },
        onClearProgress: @escaping () -> Void = {},
        contextItemsByWord: [String: J] = [:],
        contextMode: Bool = false,
        onContextReviewed: ((String) -> Void)? = nil,
        startInSpellingMode: Bool = false,
        trackMistakes: Bool = true,
        onMistakeChanged: (() -> Void)? = nil,
        close: @escaping () -> Void
    ) {
        self.core = core
        self.freshWords = words
        self.title = title
        self.persistsProgress = persistsProgress
        self.preferences = preferences
        self.onSpeak = onSpeak
        self.onSaveProgress = onSaveProgress
        self.onClearProgress = onClearProgress
        self.contextItemsByWord = contextItemsByWord
        self.contextMode = contextMode
        self.onContextReviewed = onContextReviewed
        self.trackMistakes = trackMistakes
        self.onMistakeChanged = onMistakeChanged
        self.close = close
        _practice = State(initialValue: FlashcardPractice(words: words, saved: persistsProgress ? savedProgress : nil))
        _spellingMode = State(initialValue: startInSpellingMode)
        _autoSpeak = State(initialValue: preferences?.flashcardAutoSpeak ?? "off")
    }

    private func contextItem(for word: String) -> J? {
        contextItemsByWord[word.trimmed.lowercased()]
    }

    private func highlightedSentence(_ sentence: String, word: String, matchedForm: String, fontSize: CGFloat) -> AttributedString {
        var value = AttributedString(sentence)
        value.foregroundColor = Palette.ink
        var targets = Set([word.trimmed.lowercased()])
        if !matchedForm.trimmed.isEmpty { targets.insert(matchedForm.trimmed.lowercased()) }
        var stem = word.trimmed.lowercased()
        if stem.count > 4 && stem.hasSuffix("e") { stem.removeLast() }
        let source = sentence as NSString
        let regex = try? NSRegularExpression(pattern: "[A-Za-z]+(?:['’-][A-Za-z]+)*")
        let matches = regex?.matches(in: sentence, range: NSRange(location: 0, length: source.length)) ?? []
        let hasExact = matches.contains { m in
            let token = source.substring(with: m.range).lowercased()
            return targets.contains(token)
        }
        for match in matches {
            guard let range = Range(match.range, in: sentence),
                  let start = AttributedString.Index(range.lowerBound, within: value),
                  let end = AttributedString.Index(range.upperBound, within: value) else { continue }
            let token = String(sentence[range]).lowercased()
            let hit = targets.contains(token) || (!hasExact && stem.count >= 4 && token.hasPrefix(stem))
            if hit {
                value[start..<end].backgroundColor = Palette.wash
                value[start..<end].foregroundColor = Palette.deep
                value[start..<end].font = .system(size: fontSize, weight: .bold)
            }
        }
        return value
    }

    private func blankOutWord(_ sentence: String, word: String, matchedForm: String) -> String {
        var targets = Set([word.trimmed.lowercased()])
        if !matchedForm.trimmed.isEmpty { targets.insert(matchedForm.trimmed.lowercased()) }
        var stem = word.trimmed.lowercased()
        if stem.count > 4 && stem.hasSuffix("e") { stem.removeLast() }
        let regex = try? NSRegularExpression(pattern: "[A-Za-z]+(?:['’-][A-Za-z]+)*")
        let source = sentence as NSString
        let matches = regex?.matches(in: sentence, range: NSRange(location: 0, length: source.length)) ?? []
        var result = ""
        var cursor = 0
        var replaced = false
        for match in matches {
            if match.range.location > cursor {
                result += source.substring(with: NSRange(location: cursor, length: match.range.location - cursor))
            }
            let token = source.substring(with: match.range)
            let low = token.lowercased()
            if targets.contains(low) || (!replaced && stem.count >= 4 && low.hasPrefix(stem)) {
                result += "______"
                replaced = true
            } else {
                result += token
            }
            cursor = NSMaxRange(match.range)
        }
        if cursor < source.length {
            result += source.substring(from: cursor)
        }
        return result
    }

    private var groupProgress: CGFloat {
        let total = max(1, practice.groupWordCount)
        return CGFloat(max(0, total - practice.queue.count)) / CGFloat(total)
    }

    private var canReset: Bool {
        practice.hasProgress || (practice.current == nil && !practice.hasNextGroup)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                VStack(alignment: .leading, spacing: 4) {
                    Text(title).font(.system(size: 22, weight: .semibold)).lineLimit(1)
                    Text(progressLineText)
                        .font(.system(size: 12)).foregroundStyle(Palette.muted)
                }
                Spacer()
                HStack(spacing: 8) {
                    Button("重置进度") { resetProgress(confirmRequired: true) }
                        .buttonStyle(MemoButtonStyle(compact: true))
                        .disabled(!canReset || flipping || swiping)
                        .help("清除本地保存的自测进度，从第 1 组重新开始")
                    Button("返回列表", action: close).buttonStyle(MemoButtonStyle(compact: true))
                }
            }

            HStack(spacing: 8) {
                Button("翻卡模式") {
                    spellingMode = false; revealed = false; resetSpellingInput()
                }.buttonStyle(MemoButtonStyle(accent: !spellingMode, compact: true))
                Button("拼写默写") {
                    spellingMode = true; revealed = false; resetSpellingInput()
                    if let current = practice.current {
                        Task { await loadSpellingDrill(for: current) }
                    } else {
                        spellingFocused = true
                    }
                }.buttonStyle(MemoButtonStyle(accent: spellingMode, compact: true))

                Spacer()

                if let onSpeak {
                    Text("自动朗读").font(.system(size: 11.5)).foregroundStyle(Palette.muted)
                    MemoPicker(title: "自动朗读", selection: $autoSpeak, options: [
                        ("off", "关闭"), ("en-GB", "英音"), ("en-US", "美音")
                    ], height: 28).frame(width: 82)
                        .onChange(of: autoSpeak) { value in
                            preferences?.flashcardAutoSpeak = value
                            preferences?.save()
                        }
                    Button {
                        if let current = practice.current {
                            onSpeak(current, autoSpeak == "en-GB" ? .british : .american)
                        }
                    } label: {
                        Label("朗读", systemImage: "speaker.wave.2")
                    }
                    .buttonStyle(PillButtonStyle())
                    .disabled(practice.current == nil)
                }
            }

            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Palette.wash)
                    Capsule()
                        .fill(LinearGradient(colors: [Palette.accent, Palette.deep], startPoint: .leading, endPoint: .trailing))
                        .frame(width: max(0, geo.size.width * groupProgress))
                        .animation(reduceMotion ? nil : .spring(response: 0.36, dampingFraction: 0.85), value: groupProgress)
                }
            }
            .frame(height: 6)

            if let word = practice.current {
                let data = lookup?.word == word ? lookup?.data : nil
                ZStack {
                    if practice.queue.count > 2 {
                        RoundedRectangle(cornerRadius: 15, style: .continuous)
                            .fill(Palette.wash.opacity(0.55))
                            .overlay(RoundedRectangle(cornerRadius: 15, style: .continuous).stroke(Palette.border.opacity(0.7)))
                            .padding(.horizontal, 18)
                            .offset(y: 12)
                    }
                    if practice.queue.count > 1 {
                        RoundedRectangle(cornerRadius: 15, style: .continuous)
                            .fill(Palette.wash.opacity(0.8))
                            .overlay(RoundedRectangle(cornerRadius: 15, style: .continuous).stroke(Palette.border.opacity(0.85)))
                            .padding(.horizontal, 9)
                            .offset(y: 6)
                    }

                    let angle = revealed ? 180.0 : 0
                    ZStack {
                        Group {
                            if spellingMode {
                                spellingFront(word: word, data: data)
                            } else {
                                Button(action: flip) {
                                    face(word: word, data: data, back: false)
                                }
                                .buttonStyle(.plain)
                                .noFocusRing()
                            }
                        }
                        .modifier(FlashcardFaceTurn(angle: angle, back: false))
                        .accessibilityHidden(revealed)

                        face(word: word, data: data, back: true)
                            .modifier(FlashcardFaceTurn(angle: angle, back: true))
                            .accessibilityHidden(!revealed)
                    }
                    .contentShape(RoundedRectangle(cornerRadius: 15, style: .continuous))
                    .offset(x: swipeOffset)
                    .rotationEffect(.degrees(swipeAngle))
                    .opacity(swipeOpacity)

                    if !spellingMode || revealed {
                        Button("") { flip() }
                            .keyboardShortcut(.space, modifiers: [])
                            .opacity(0)
                            .frame(width: 0, height: 0)
                            .accessibilityHidden(true)
                    }
                }

                ZStack {
                    if revealed {
                        HStack(spacing: 10) {
                            Button("认识") { answer(.known) }
                                .buttonStyle(FlashcardRatingStyle(tone: .known))
                                .keyboardShortcut("1", modifiers: [])
                            Button("模糊") { answer(.vague) }
                                .buttonStyle(FlashcardRatingStyle(tone: .vague))
                                .keyboardShortcut("2", modifiers: [])
                            Button("忘记") { answer(.forgotten) }
                                .buttonStyle(FlashcardRatingStyle(tone: .forgotten))
                                .keyboardShortcut("3", modifiers: [])
                        }
                        .disabled(flipping || swiping)
                        .transition(reduceMotion ? .opacity : .move(edge: .bottom).combined(with: .opacity))
                    } else {
                        Text(spellingMode
                             ? "根据释义输入拼写，拼对自动翻面；没拼完点「核对拼写」获取提示"
                             : "先点击卡片翻面核对释义（空格键），再选择掌握程度")
                            .font(.callout)
                            .foregroundStyle(Palette.muted)
                            .padding(.horizontal, 14).padding(.vertical, 7)
                            .background(Palette.wash.opacity(0.7), in: Capsule())
                            .transition(.opacity)
                    }
                }
                .frame(maxWidth: .infinity, minHeight: 38)
                .animation(reduceMotion ? nil : .spring(response: 0.28, dampingFraction: 0.82), value: revealed)
            } else {
                VStack(spacing: 13) {
                    Image(systemName: "checkmark.circle.fill").font(.system(size: 36)).foregroundStyle(Palette.deep)
                    Text(practice.hasNextGroup ? "本组已完成" : "全部练习完成").font(.title2.weight(.semibold))
                    HStack(spacing: 10) {
                        if !practice.hasNextGroup {
                            Button("重新开始") { resetProgress(confirmRequired: false) }
                                .buttonStyle(MemoButtonStyle())
                        }
                        Button(practice.hasNextGroup ? "下一组" : "完成") {
                            if practice.startNextGroup() { persistProgress() }
                            else { close() }
                        }.buttonStyle(MemoButtonStyle(accent: true))
                    }
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            Text(persistsProgress
                ? "快捷键：空格翻卡 · 1 认识 · 2 模糊 · 3 忘记（自测进度自动保存到本地，可随时重置）"
                : "快捷键：空格翻卡 · 1 认识 · 2 模糊 · 3 忘记（当前为筛选结果自测，不覆盖全部自测进度）")
                .font(.footnote).foregroundStyle(Palette.muted)
        }
        .noFocusRing()
        .task(id: practice.current) {
            resetSpellingInput(resetMistakeState: true)
            activeDrill = nil
            microChoices = []
            currentMistakeWeight = nil
            guard let word = practice.current else { return }
            async let lookupReq = core.request("dictionary.lookup", ["word": .string(word)])
            async let weightReq = fetchMistakeWeight(for: word)
            await loadSpellingDrill(for: word)
            let result = try? await lookupReq
            let weight = await weightReq
            guard !Task.isCancelled, practice.current == word else { return }
            lookup = (word, result ?? .null)
            currentMistakeWeight = weight
        }.onDisappear {
            flipTask?.cancel()
            autoFlipTask?.cancel()
        }
    }

    private func fetchMistakeWeight(for word: String) async -> Int? {
        guard trackMistakes else { return nil }
        guard let rec = try? await core.request("mistakes.get", ["word": .string(word), "category": .string("spelling")]),
              rec != .null else { return nil }
        let weight = rec["weight"].integer
        return weight > 0 ? weight : nil
    }

    private func loadSpellingDrill(for word: String) async {
        let drill = try? await core.request("spelling.getDrill", ["word": .string(word)])
        guard !Task.isCancelled, practice.current == word else { return }
        if let drill, drill != .null, !drill["correctOption"].text.isEmpty {
            activeDrill = drill
            let correct = drill["correctOption"].text
            let wrong = drill["wrongOption"].text
            microChoices = Bool.random() ? [correct, wrong] : [wrong, correct]
            microDrillDone = false
        } else {
            activeDrill = nil
            microChoices = []
            if spellingMode { spellingFocused = true }
        }
    }

    private func pickMicroOption(_ choice: String, drill: J) {
        let right = choice.caseInsensitiveCompare(drill["correctOption"].text) == .orderedSame
        microDrillWasCorrect = right
        if !right { hadMistakeOnCurrentWord = true }
        let exp = drill["explanation"].text
        microDrillFeedback = right
            ? "✓ 选对了！\(exp) — 现在完整默写一遍："
            : "✗ 差点又写错！\(exp) — 记住它，再完整默写一遍："
        microDrillDone = true
        spellingFocused = true
    }

    private func spellingFront(word: String, data: J?) -> some View {
        let shape = RoundedRectangle(cornerRadius: 15, style: .continuous)
        let phonetic = data?["entry"]["phonetic"].text ?? ""
        let rawTestMeaning = data?["testMeaning"].text.decodedLines ?? ""
        let localMeaning = rawTestMeaning.isEmpty
            ? fallbackSanitizeTestMeaning(data?["entry"]["translation"].text.decodedLines ?? "")
            : rawTestMeaning
        let ctx = contextItem(for: word)
        return VStack(spacing: 12) {
            if let drill = activeDrill, !microDrillDone, microChoices.count == 2 {
                Text("拼写易错点 · 曾错 \(drill["mistakeCount"].integer) 次")
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundStyle(Color(red: 0.65, green: 0.34, blue: 0.03))
                    .padding(.horizontal, 12).padding(.vertical, 4)
                    .background(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.15), in: Capsule())
                    .overlay(Capsule().strokeBorder(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.38), lineWidth: 0.85))

                Text(localMeaning.isEmpty
                     ? "上次你写成了「\(drill["lastWrongInput"].text)」"
                     : "\(localMeaning)（上次写成了 \(drill["lastWrongInput"].text)）")
                    .font(.system(size: 13))
                    .foregroundStyle(Palette.muted)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
                    .padding(.horizontal, 12)

                Text(drill["contextMask"].text)
                    .font(.system(size: 30, weight: .bold))
                    .foregroundStyle(Palette.ink)
                    .padding(.top, 2)

                Text(drill["question"].text)
                    .font(.system(size: 15.5, weight: .semibold))
                    .foregroundStyle(Palette.deep)
                    .multilineTextAlignment(.center)

                HStack(spacing: 12) {
                    ForEach(Array(microChoices.enumerated()), id: \.offset) { idx, opt in
                        Button {
                            pickMicroOption(opt, drill: drill)
                        } label: {
                            HStack(spacing: 8) {
                                Text("\(idx + 1)")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundStyle(Palette.deep)
                                    .frame(width: 20, height: 20)
                                    .background(Palette.wash, in: Circle())
                                Text(opt)
                                    .font(.system(size: 19, weight: .bold))
                                    .foregroundStyle(Palette.ink)
                            }
                            .frame(minWidth: 120)
                            .padding(.horizontal, 18).padding(.vertical, 10)
                            .background(Palette.paper, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.border, lineWidth: 1))
                        }
                        .buttonStyle(.plain)
                        .noFocusRing()
                        .keyboardShortcut(revealed ? nil : KeyboardShortcut(idx == 0 ? "1" : "2", modifiers: []))
                    }
                }
                .padding(.top, 4)

                Button("跳过，直接默写") {
                    microDrillDone = true
                    spellingFocused = true
                }
                .buttonStyle(.plain)
                .font(.system(size: 11.5))
                .foregroundStyle(Palette.muted)
                .padding(.top, 2)
            } else {
                Text("第 \(practice.groupNumber) 组 · 拼写默写")
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundStyle(Palette.deep)
                    .padding(.horizontal, 11).padding(.vertical, 4)
                    .background(Palette.paper.opacity(0.88), in: Capsule())
                    .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.8))

                if !microDrillFeedback.isEmpty {
                    Text(microDrillFeedback)
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(microDrillWasCorrect ? Color(red: 0.06, green: 0.54, blue: 0.36) : Color(red: 0.71, green: 0.34, blue: 0.04))
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 12).padding(.vertical, 5)
                        .background((microDrillWasCorrect ? Color(red: 0.08, green: 0.72, blue: 0.48) : Color(red: 0.98, green: 0.62, blue: 0.09)).opacity(0.14), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                }

                Text(localMeaning.isEmpty ? "请听发音并拼写该单词" : localMeaning)
                    .font(.system(size: 18, weight: .semibold))
                    .lineSpacing(4)
                    .multilineTextAlignment(.center)
                    .lineLimit(3)
                    .padding(.horizontal, 12)

                if let ctx {
                    Text(blankOutWord(ctx["sentence"].text, word: word, matchedForm: ctx["matchedForm"].text))
                        .font(.system(size: 13.5))
                        .foregroundStyle(Palette.muted)
                        .multilineTextAlignment(.center)
                        .lineLimit(3)
                        .padding(.horizontal, 12).padding(.vertical, 7)
                        .background(Palette.paper.opacity(0.85), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
                }

                if !phonetic.isEmpty {
                    Text("/\(phonetic.trimmingCharacters(in: CharacterSet(charactersIn: "/")))/")
                        .font(.system(size: 14.5)).foregroundStyle(Palette.muted)
                }

                TextField("输入英文拼写…", text: $typedSpelling)
                    .textFieldStyle(.plain)
                    .font(.system(size: 20, weight: .medium))
                    .multilineTextAlignment(.center)
                    .focused($spellingFocused)
                    .onSubmit { submitSpelling(word) }
                    .onChange(of: typedSpelling) { newValue in
                        handleSpellingChanged(newValue, word: word)
                    }
                    .padding(.horizontal, 14).frame(width: 330, height: 44)
                    .controlSurface()
                    .overlay(
                        RoundedRectangle(cornerRadius: 9, style: .continuous)
                            .strokeBorder(spellingBorderColor, lineWidth: spellingBorderColor == .clear ? 0 : 1.4)
                    )

                if !spellingClue.isEmpty {
                    Text(spellingClue)
                        .font(.system(size: 12.5, weight: .medium))
                        .foregroundStyle(spellingClueForeground)
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 12).padding(.vertical, 5)
                        .background(spellingClueBackground, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(spellingClueBorder, lineWidth: 0.9))
                }

                HStack(spacing: 8) {
                    Button("核对拼写") { submitSpelling(word) }
                        .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                    Button("提示字母") {
                        let maxReveal = max(1, word.count - 1)
                        spellingHintLevel = min(maxReveal, spellingHintLevel + 1)
                        let mask = maskSpellingWord(word, revealCount: spellingHintLevel)
                        spellingClue = "提示：\(mask)（共 \(word.count) 字母）"
                        spellingClueTone = "info"
                        spellingFocused = true
                    }.buttonStyle(MemoButtonStyle(compact: true))
                    Button("直接看答案") {
                        autoFlipTask?.cancel()
                        hadMistakeOnCurrentWord = true
                        spellingFeedback = ""
                        flip()
                    }.buttonStyle(MemoButtonStyle(compact: true))
                }
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            shape
                .fill(LinearGradient(colors: [Palette.paper, Palette.wash], startPoint: .topLeading, endPoint: .bottomTrailing))
                .shadow(color: Color(red: 0.06, green: 0.25, blue: 0.40).opacity(0.09), radius: 12, y: 4)
        }
        .overlay(shape.stroke(Palette.border, lineWidth: 0.9))
    }

    private func fallbackSanitizeTestMeaning(_ text: String) -> String {
        guard !text.isEmpty else { return "" }
        return text.replacingOccurrences(
            of: #"[（(\[【][^（）()\[\]【】]*[A-Za-z]{2,}[^（）()\[\]【】]*[）)\]】]"#,
            with: "",
            options: .regularExpression
        ).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private var spellingBorderColor: Color {
        if spellingClue.isEmpty { return .clear }
        switch spellingClueTone {
        case "success": return Color(red: 0.06, green: 0.72, blue: 0.51)
        case "warn": return Color(red: 0.96, green: 0.62, blue: 0.04)
        default: return .clear
        }
    }

    private var spellingClueForeground: Color {
        switch spellingClueTone {
        case "success": return Color(red: 0.06, green: 0.54, blue: 0.36)
        case "warn": return Color(red: 0.71, green: 0.34, blue: 0.04)
        default: return Palette.deep
        }
    }

    private var spellingClueBackground: Color {
        switch spellingClueTone {
        case "success": return Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.14)
        case "warn": return Color(red: 0.98, green: 0.62, blue: 0.09).opacity(0.14)
        default: return Palette.paper.opacity(0.9)
        }
    }

    private var spellingClueBorder: Color {
        switch spellingClueTone {
        case "success": return Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.35)
        case "warn": return Color(red: 0.95, green: 0.62, blue: 0.14).opacity(0.42)
        default: return Palette.border.opacity(0.9)
        }
    }

    private func handleSpellingChanged(_ value: String, word: String) {
        autoFlipTask?.cancel()
        guard spellingMode, !revealed, !flipping, !swiping else { return }
        let trimmed = value.trimmed
        if trimmed.caseInsensitiveCompare(word) == .orderedSame {
            spellingCorrect = true
            spellingFeedback = "✓ 拼写正确"
            spellingClue = "✓ 拼写正确，自动翻面…"
            spellingClueTone = "success"
            let cleanPass = !hadMistakeOnCurrentWord && spellingHintLevel == 0
            autoFlipTask = Task { @MainActor in
                try? await Task.sleep(nanoseconds: 140_000_000)
                guard !Task.isCancelled, spellingMode, !revealed, !flipping, !swiping,
                      practice.current == word,
                      typedSpelling.trimmed.caseInsensitiveCompare(word) == .orderedSame else { return }
                if cleanPass {
                    _ = try? await core.request("spelling.recordSuccess", ["word": .string(word)])
                    await recordCardMistakeRightAsync(word)
                }
                flip()
            }
        } else if spellingClueTone == "warn" || spellingClueTone == "success" {
            spellingClueTone = "info"
        }
    }

    private func submitSpelling(_ word: String) {
        autoFlipTask?.cancel()
        let trimmed = typedSpelling.trimmed
        let hasValidLetters = !trimmed.isEmpty && trimmed.contains { $0.isASCII && $0.isLetter }
        guard hasValidLetters else {
            spellingClue = trimmed.isEmpty ? "请先输入单词拼写后再核对" : "请输入英文字母后再核对拼写"
            spellingClueTone = "info"
            spellingFocused = true
            return
        }
        if trimmed.caseInsensitiveCompare(word) == .orderedSame {
            if !hadMistakeOnCurrentWord && spellingHintLevel == 0 {
                Task {
                    _ = try? await core.request("spelling.recordSuccess", ["word": .string(word)])
                    await recordCardMistakeRightAsync(word)
                }
            }
            spellingCorrect = true
            spellingFeedback = "✓ 拼写正确"
            flip()
            return
        }

        hadMistakeOnCurrentWord = true
        let willRecordMistake = trackMistakes && !mistakeEvaluatedOnCard
        recordCardMistakeWrong(word, reason: "拼写错误", wrongInput: trimmed)
        let (message, isProgress) = buildSpellingHint(word: word, typed: trimmed)
        spellingClue = willRecordMistake ? "\(message) · 已记入拼写错题" : message
        spellingClueTone = isProgress ? "info" : "warn"
        spellingFocused = true
        Task { @MainActor in
            if let rec = try? await core.request("spelling.recordMistake", ["word": .string(word), "typed": .string(trimmed)]),
               rec != .null, practice.current == word, !revealed,
               !spellingClue.contains("已记入拼写错题"), !spellingClue.contains("已记下易错点") {
                spellingClue += " · 已记下易错点"
            }
        }
    }

    private func maskSpellingWord(_ word: String, revealCount: Int) -> String {
        let chars = Array(word)
        guard !chars.isEmpty else { return "" }
        let limit = min(max(1, revealCount), max(1, chars.count - 1))
        return String(chars.enumerated().map { idx, ch in
            (idx < limit || !ch.isLetter) ? ch : "_"
        })
    }

    private func commonPrefixLength(_ target: String, _ typed: String) -> Int {
        let a = Array(target.lowercased())
        let b = Array(typed.lowercased())
        let limit = min(a.count, b.count)
        var matched = 0
        while matched < limit && a[matched] == b[matched] {
            matched += 1
        }
        return matched
    }

    private func buildSpellingHint(word: String, typed: String) -> (String, Bool) {
        let maxReveal = max(1, word.count - 1)
        if typed.isEmpty {
            spellingHintLevel = min(maxReveal, spellingHintLevel + 1)
            return ("还没输入哦 · 提示：\(maskSpellingWord(word, revealCount: spellingHintLevel))（共 \(word.count) 字母），先试着想想", true)
        }

        let matched = commonPrefixLength(word, typed)
        if typed.count < word.count && matched == typed.count {
            let remaining = word.count - typed.count
            if remaining == 1 {
                return ("前 \(matched) 位全对！只差最后 1 个字母（共 \(word.count) 字母），再想想～", true)
            }
            spellingHintLevel = min(maxReveal, max(spellingHintLevel + 1, matched + 1))
            return ("前 \(matched) 位全对，还差 \(remaining) 个字母 · 提示：\(maskSpellingWord(word, revealCount: spellingHintLevel))", true)
        }

        if typed.count > word.count && matched == word.count {
            return ("前面 \(word.count) 位已经拼对啦，末尾多输入了 \(typed.count - word.count) 个字母，删掉再试试", false)
        }

        if matched > 0 {
            if matched >= word.count - 1 {
                return ("前 \(matched) 位都对啦，只差最后 1 位有误（共 \(word.count) 字母），再想想～", false)
            }
            spellingHintLevel = min(maxReveal, max(spellingHintLevel + 1, matched + 1))
            return ("前 \(matched) 位正确，第 \(matched + 1) 位有误 · 提示：\(maskSpellingWord(word, revealCount: spellingHintLevel))（共 \(word.count) 字母）", false)
        }

        spellingHintLevel = min(maxReveal, max(1, spellingHintLevel + 1))
        return ("第 1 位有误 · 提示：\(maskSpellingWord(word, revealCount: spellingHintLevel))（共 \(word.count) 字母），再想想看", false)
    }

    private var progressLineText: String {
        var parts = ["第 \(practice.groupNumber) / \(practice.groupCount) 组", "剩余 \(practice.queue.count) / \(practice.groupWordCount) 词", "已掌握 \(practice.masteredCount) / \(practice.totalWordCount)"]
        if spellingMode, let weight = currentMistakeWeight, weight > 0 {
            let level = weight >= 6 ? "重点易错" : weight >= 3 ? "待巩固" : "快掌握"
            parts.append(level)
        }
        if !mistakeNotice.isEmpty {
            parts.append(mistakeNotice)
        }
        return parts.joined(separator: " · ")
    }

    private func recordCardMistakeWrong(_ word: String, reason: String, wrongInput: String? = nil) {
        guard trackMistakes, !mistakeEvaluatedOnCard else { return }
        mistakeEvaluatedOnCard = true
        let ctx = contextItem(for: word)
        var params: [String: J] = [
            "word": .string(word),
            "reason": .string(reason)
        ]
        if let wrongInput, !wrongInput.trimmed.isEmpty { params["wrongInput"] = .string(wrongInput.trimmed) }
        if let s = ctx?["sentence"].text, !s.trimmed.isEmpty { params["contextSentence"] = .string(s) }
        if let t = ctx?["translation"].text, !t.trimmed.isEmpty { params["contextTranslation"] = .string(t) }
        Task { @MainActor in
            if let rec = try? await core.request("mistakes.recordWrong", params), rec != .null {
                let w = rec["weight"].integer
                if practice.current == word { currentMistakeWeight = w }
                mistakeNotice = "已记入拼写错题"
                onMistakeChanged?()
            }
        }
    }

    @MainActor private func recordCardMistakeRightAsync(_ word: String) async {
        guard trackMistakes, !mistakeEvaluatedOnCard else { return }
        mistakeEvaluatedOnCard = true
        if let res = try? await core.request("mistakes.recordRight", ["word": .string(word), "category": .string("spelling")]),
           res["wasInBook"].flag {
            if res["removed"].flag {
                if practice.current == word { currentMistakeWeight = nil }
                mistakeNotice = "已掌握，自动移出拼写错题 ✓"
            } else {
                let rem = res["remainingWeight"].integer
                if practice.current == word { currentMistakeWeight = rem }
                mistakeNotice = "拼写错题已巩固一次"
            }
            onMistakeChanged?()
        }
    }

    private func resetSpellingInput(resetMistakeState: Bool = false) {
        autoFlipTask?.cancel()
        typedSpelling = ""
        spellingFeedback = ""
        spellingCorrect = false
        spellingClue = ""
        spellingClueTone = "info"
        spellingHintLevel = 0
        hadMistakeOnCurrentWord = false
        microDrillFeedback = ""
        microDrillWasCorrect = false
        microDrillDone = false
        if resetMistakeState {
            mistakeEvaluatedOnCard = false
        }
    }

    private func face(word: String, data: J?, back: Bool) -> some View {
        let shape = RoundedRectangle(cornerRadius: 15, style: .continuous)
        let phonetic = data?["entry"]["phonetic"].text ?? ""
        let ctx = contextItem(for: word)
        return Group {
            if back {
                VStack(alignment: .leading, spacing: 11) {
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Text(word).font(.system(size: 28, weight: .bold))
                        if !phonetic.isEmpty {
                            Text("/\(phonetic.trimmingCharacters(in: CharacterSet(charactersIn: "/")))/")
                                .font(.system(size: 14.5)).foregroundStyle(Palette.muted)
                        }
                        if !spellingFeedback.isEmpty {
                            Text(spellingFeedback)
                                .font(.system(size: 11.5, weight: .semibold))
                                .foregroundStyle(spellingCorrect ? Color(red: 0.06, green: 0.54, blue: 0.36) : Color(red: 0.78, green: 0.16, blue: 0.26))
                                .padding(.horizontal, 8).padding(.vertical, 2.5)
                                .background((spellingCorrect ? Color(red: 0.08, green: 0.72, blue: 0.48) : Color(red: 0.96, green: 0.25, blue: 0.37)).opacity(0.14), in: Capsule())
                        }
                        Spacer(minLength: 6)
                        Button("点击翻回", action: flip)
                            .buttonStyle(.plain)
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundStyle(Palette.deep)
                            .padding(.horizontal, 9).padding(.vertical, 3)
                            .background(Palette.paper.opacity(0.88), in: Capsule())
                            .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.8))
                    }
                    Divider().overlay(Palette.border.opacity(0.85))
                    ScrollView {
                        VStack(alignment: .leading, spacing: 9) {
                            Text("中文释义").font(.system(size: 12, weight: .semibold)).foregroundStyle(Palette.deep)
                            WordListMeaning(core: core, word: word, local: data?["entry"]["translation"].text, concealed: !revealed, fontSize: 16.5) { _ in }
                                .lineSpacing(4)
                                .id(word)
                            if let ctx {
                                VStack(alignment: .leading, spacing: 5) {
                                    Text("收藏例句 · \(ctx["source"].text)")
                                        .font(.system(size: 11.5, weight: .semibold))
                                        .foregroundStyle(Palette.deep)
                                    Text(highlightedSentence(ctx["sentence"].text, word: word, matchedForm: ctx["matchedForm"].text, fontSize: 14.5))
                                        .font(.system(size: 14.5))
                                        .lineSpacing(4)
                                        .fixedSize(horizontal: false, vertical: true)
                                    if !ctx["translation"].text.isEmpty {
                                        Text(ctx["translation"].text)
                                            .font(.system(size: 13))
                                            .foregroundStyle(Palette.muted)
                                            .lineSpacing(3)
                                            .fixedSize(horizontal: false, vertical: true)
                                    }
                                }
                                .padding(11)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .background(Palette.paper.opacity(0.92), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                                .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).stroke(Palette.accent.opacity(0.35)))
                                .padding(.top, 4)
                            }
                            if let firstNote = data?["notes"].list.first {
                                let breakdowns = firstNote["breakdowns"].list
                                if !breakdowns.isEmpty {
                                    MnemonicPartsView(breakdowns: [breakdowns[0]])
                                        .padding(10)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                        .background(Palette.paper.opacity(0.92), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                                        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).stroke(Palette.border.opacity(0.85)))
                                        .padding(.top, 4)
                                } else if !firstNote["text"].text.isEmpty {
                                    HStack(alignment: .top, spacing: 9) {
                                        Text("记")
                                            .font(.system(size: 11, weight: .bold))
                                            .foregroundStyle(Palette.deep)
                                            .frame(width: 22, height: 22)
                                            .background(Palette.accent.opacity(0.2), in: RoundedRectangle(cornerRadius: 6, style: .continuous))
                                        Text(firstNote["text"].text).font(.system(size: 14.5)).lineSpacing(4).foregroundStyle(Palette.ink.opacity(0.9)).fixedSize(horizontal: false, vertical: true)
                                    }
                                    .padding(11)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .background(Palette.paper.opacity(0.9), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                                    .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).stroke(Palette.border.opacity(0.85)))
                                    .padding(.top, 4)
                                }
                            }
                        }.frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            } else if contextMode, let ctx {
                VStack(spacing: 12) {
                    Text("第 \(practice.groupNumber) 组 · 例句自测（\(ctx["source"].text)）")
                        .font(.system(size: 11.5, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 11).padding(.vertical, 4)
                        .background(Palette.paper.opacity(0.88), in: Capsule())
                        .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.8))
                    Text("这里的「\(word)」是什么意思？")
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                    Text(highlightedSentence(ctx["sentence"].text, word: word, matchedForm: ctx["matchedForm"].text, fontSize: 17.5))
                        .font(.system(size: 17.5))
                        .lineSpacing(5)
                        .multilineTextAlignment(.center)
                        .padding(14)
                        .frame(maxWidth: .infinity)
                        .background(Palette.paper.opacity(0.92), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.accent.opacity(0.32), lineWidth: 0.9))
                    if !phonetic.isEmpty {
                        Text("/\(phonetic.trimmingCharacters(in: CharacterSet(charactersIn: "/")))/")
                            .font(.system(size: 14.5)).foregroundStyle(Palette.muted)
                    }
                    Label("点击翻卡核对释义与整句译文", systemImage: "arrow.clockwise")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 13).padding(.vertical, 6)
                        .background(Palette.paper.opacity(0.88), in: Capsule())
                }
            } else {
                VStack(spacing: 12) {
                    Text("第 \(practice.groupNumber) 组 · 点击翻面")
                        .font(.system(size: 11.5, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 11).padding(.vertical, 4)
                        .background(Palette.paper.opacity(0.88), in: Capsule())
                        .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.8))
                    Text(word).font(.system(size: 42, weight: .bold)).multilineTextAlignment(.center)
                    if !phonetic.isEmpty {
                        Text("/\(phonetic.trimmingCharacters(in: CharacterSet(charactersIn: "/")))/")
                            .font(.system(size: 15.5)).foregroundStyle(Palette.muted)
                    }
                    if let ctx {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("收藏例句 · \(ctx["source"].text)")
                                .font(.system(size: 11, weight: .semibold))
                                .foregroundStyle(Palette.deep)
                            Text(highlightedSentence(ctx["sentence"].text, word: word, matchedForm: ctx["matchedForm"].text, fontSize: 13.5))
                                .font(.system(size: 13.5))
                                .lineLimit(2)
                        }
                        .padding(.horizontal, 12).padding(.vertical, 8)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Palette.paper.opacity(0.88), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
                    }
                    Label("点击翻卡核对释义", systemImage: "arrow.clockwise")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 13).padding(.vertical, 6)
                        .background(Palette.paper.opacity(0.88), in: Capsule())
                        .padding(.top, 6)
                }
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            shape
                .fill(LinearGradient(colors: [Palette.paper, Palette.wash], startPoint: .topLeading, endPoint: .bottomTrailing))
                .shadow(color: Color(red: 0.06, green: 0.25, blue: 0.40).opacity(0.09), radius: 12, y: 4)
        }
        .overlay(shape.stroke(back ? Palette.accent.opacity(0.65) : Palette.border, lineWidth: back ? 1.1 : 0.9))
    }

    private func flip() {
        autoFlipTask?.cancel()
        guard !flipping, !swiping, let current = practice.current else { return }
        let willReveal = !revealed
        if !willReveal {
            resetSpellingInput()
        }
        if willReveal, let onSpeak, autoSpeak == "en-GB" || autoSpeak == "en-US" {
            onSpeak(current, autoSpeak == "en-GB" ? .british : .american)
        }
        if reduceMotion {
            revealed.toggle()
            if !willReveal && spellingMode { spellingFocused = true }
            return
        }
        flipping = true
        withAnimation(.easeInOut(duration: 0.26)) { revealed.toggle() }
        flipTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 270_000_000)
            if !Task.isCancelled {
                flipping = false
                if !willReveal && spellingMode { spellingFocused = true }
            }
        }
    }

    private func answer(_ value: FlashcardPractice.Answer) {
        guard revealed, !flipping, !swiping else { return }
        if let current = practice.current {
            if let ctx = contextItem(for: current), !ctx["id"].text.isEmpty {
                onContextReviewed?(ctx["id"].text)
            }
        }
        autoFlipTask?.cancel()
        if reduceMotion {
            practice.answer(value)
            persistProgress()
            revealed = false
            resetSpellingInput(resetMistakeState: true)
            if spellingMode && practice.current != nil { spellingFocused = true }
            return
        }
        swiping = true
        let direction: CGFloat = value == .forgotten ? -1 : 1
        withAnimation(.easeIn(duration: 0.16)) {
            swipeOffset = direction * 110
            swipeAngle = Double(direction) * 5.5
            swipeOpacity = 0
        }
        flipTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 170_000_000)
            guard !Task.isCancelled else { return }
            practice.answer(value)
            persistProgress()
            revealed = false
            resetSpellingInput(resetMistakeState: true)
            swipeOffset = 0
            swipeAngle = 0
            withAnimation(.spring(response: 0.24, dampingFraction: 0.88)) {
                swipeOpacity = 1
            }
            swiping = false
            if spellingMode && practice.current != nil { spellingFocused = true }
        }
    }

    private func persistProgress() {
        if persistsProgress { onSaveProgress(practice.snapshot()) }
    }

    @MainActor private func resetProgress(confirmRequired: Bool) {
        guard !flipping, !swiping else { return }
        if confirmRequired, !confirm("重置自测进度？", "将清除本地保存的自测进度，并从第 1 组重新开始。", action: "重置") {
            return
        }
        flipTask?.cancel()
        practice.reset(words: freshWords)
        revealed = false
        mistakeNotice = ""
        resetSpellingInput(resetMistakeState: true)
        swipeOffset = 0
        swipeAngle = 0
        swipeOpacity = 1
        if persistsProgress { onClearProgress() }
    }
}

private enum FlashcardRatingTone {
    case known, vague, forgotten
}

private struct FlashcardRatingStyle: ButtonStyle {
    let tone: FlashcardRatingTone
    @Environment(\.isEnabled) private var enabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func makeBody(configuration: Configuration) -> some View {
        let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
        let fg: Color
        let bg: Color
        let stroke: Color
        switch tone {
        case .known:
            fg = .white
            bg = Color(red: 0.11, green: 0.67, blue: 0.46)
            stroke = Color.clear
        case .vague:
            fg = Color(red: 0.71, green: 0.34, blue: 0.04)
            bg = Color(red: 1.0, green: 0.96, blue: 0.89)
            stroke = Color(red: 0.95, green: 0.79, blue: 0.49)
        case .forgotten:
            fg = Color(red: 0.78, green: 0.16, blue: 0.26)
            bg = Color(red: 1.0, green: 0.94, blue: 0.95)
            stroke = Color(red: 0.96, green: 0.68, blue: 0.72)
        }
        return configuration.label
            .font(.system(size: 13, weight: .semibold))
            .frame(maxWidth: .infinity, minHeight: 35)
            .foregroundStyle(fg)
            .background(bg.opacity(configuration.isPressed ? 0.82 : 1), in: shape)
            .overlay(shape.strokeBorder(stroke, lineWidth: 0.9))
            .opacity(enabled ? 1 : 0.42)
            .scaleEffect(enabled && configuration.isPressed && !reduceMotion ? 0.97 : 1)
            .animation(reduceMotion ? nil : .spring(response: 0.2, dampingFraction: 0.78), value: configuration.isPressed)
            .noFocusRing()
    }
}

private struct FlashcardFaceTurn: AnimatableModifier {
    var angle: Double
    let back: Bool
    var animatableData: Double {
        get { angle }
        set { angle = newValue }
    }

    func body(content: Content) -> some View {
        let visible = (angle >= 90) == back
        content.rotation3DEffect(.degrees(angle - (back ? 180 : 0)), axis: (x: 0, y: 1, z: 0), perspective: 0.55)
            .opacity(visible ? 1 : 0)
            .allowsHitTesting(visible)
    }
}
