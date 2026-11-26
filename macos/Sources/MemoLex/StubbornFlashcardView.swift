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
    let close: () -> Void
    @State private var practice: FlashcardPractice
    @State private var revealed = false
    @State private var flipping = false
    @State private var swiping = false
    @State private var swipeOffset: CGFloat = 0
    @State private var swipeAngle: Double = 0
    @State private var swipeOpacity: Double = 1
    @State private var flipTask: Task<Void, Never>?
    @State private var lookup: (word: String, data: J)?
    @State private var spellingMode = false
    @State private var typedSpelling = ""
    @State private var spellingFeedback = ""
    @State private var spellingCorrect = false
    @State private var spellingClue = ""
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
        self.close = close
        _practice = State(initialValue: FlashcardPractice(words: words, saved: persistsProgress ? savedProgress : nil))
        _autoSpeak = State(initialValue: preferences?.flashcardAutoSpeak ?? "off")
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
                    Text("第 \(practice.groupNumber) / \(practice.groupCount) 组 · 剩余 \(practice.queue.count) / \(practice.groupWordCount) 词 · 已掌握 \(practice.masteredCount) / \(practice.totalWordCount)")
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
                    spellingMode = true; revealed = false; resetSpellingInput(); spellingFocused = true
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
                             ? "根据释义输入英文拼写并按 Enter 核对，或点击「直接看答案」"
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
            resetSpellingInput()
            guard let word = practice.current else { return }
            if let onSpeak, autoSpeak == "en-GB" || autoSpeak == "en-US" {
                onSpeak(word, autoSpeak == "en-GB" ? .british : .american)
            }
            if spellingMode { spellingFocused = true }
            let result = try? await core.request("dictionary.lookup", ["word": .string(word)])
            guard !Task.isCancelled, practice.current == word else { return }
            lookup = (word, result ?? .null)
        }.onDisappear { flipTask?.cancel() }
    }

    private func spellingFront(word: String, data: J?) -> some View {
        let shape = RoundedRectangle(cornerRadius: 15, style: .continuous)
        let phonetic = data?["entry"]["phonetic"].text ?? ""
        let localMeaning = data?["entry"]["translation"].text.decodedLines ?? ""
        return VStack(spacing: 12) {
            Text("第 \(practice.groupNumber) 组 · 拼写默写")
                .font(.system(size: 11.5, weight: .semibold))
                .foregroundStyle(Palette.deep)
                .padding(.horizontal, 11).padding(.vertical, 4)
                .background(Palette.paper.opacity(0.88), in: Capsule())
                .overlay(Capsule().strokeBorder(Palette.border, lineWidth: 0.8))

            Text(localMeaning.isEmpty ? "请听发音并拼写该单词" : localMeaning)
                .font(.system(size: 18, weight: .semibold))
                .lineSpacing(4)
                .multilineTextAlignment(.center)
                .lineLimit(3)
                .padding(.horizontal, 12)

            if !phonetic.isEmpty {
                Text("/\(phonetic.trimmingCharacters(in: CharacterSet(charactersIn: "/")))/")
                    .font(.system(size: 14.5)).foregroundStyle(Palette.muted)
            }

            TextField("输入英文拼写…", text: $typedSpelling)
                .textFieldStyle(.plain)
                .font(.system(size: 19, weight: .medium))
                .multilineTextAlignment(.center)
                .focused($spellingFocused)
                .onSubmit { submitSpelling(word) }
                .padding(.horizontal, 14).frame(width: 320, height: 40)
                .controlSurface()

            if !spellingClue.isEmpty {
                Text(spellingClue)
                    .font(.system(size: 13, design: .monospaced))
                    .foregroundStyle(Palette.deep)
            }

            HStack(spacing: 8) {
                Button("核对拼写") { submitSpelling(word) }
                    .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                Button("提示首字母") {
                    if word.count <= 2 {
                        spellingClue = "\(word.prefix(1))…"
                    } else {
                        spellingClue = "\(word.prefix(1))\(String(repeating: "_", count: max(1, word.count - 2)))\(word.suffix(1))（\(word.count) 字母）"
                    }
                    spellingFocused = true
                }.buttonStyle(MemoButtonStyle(compact: true))
                Button("直接看答案") {
                    spellingFeedback = ""
                    flip()
                }.buttonStyle(MemoButtonStyle(compact: true))
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(
            LinearGradient(colors: [Palette.paper, Palette.wash], startPoint: .topLeading, endPoint: .bottomTrailing),
            in: shape
        )
        .overlay(shape.stroke(Palette.border, lineWidth: 0.9))
        .shadow(color: Color(red: 0.06, green: 0.25, blue: 0.40).opacity(0.09), radius: 14, y: 5)
    }

    private func submitSpelling(_ word: String) {
        let trimmed = typedSpelling.trimmed
        if trimmed.isEmpty { flip(); return }
        spellingCorrect = trimmed.caseInsensitiveCompare(word) == .orderedSame
        spellingFeedback = spellingCorrect ? "✓ 拼写正确" : "✕ 你的拼写：\(trimmed)"
        flip()
    }

    private func resetSpellingInput() {
        typedSpelling = ""
        spellingFeedback = ""
        spellingCorrect = false
        spellingClue = ""
    }

    private func face(word: String, data: J?, back: Bool) -> some View {
        let shape = RoundedRectangle(cornerRadius: 15, style: .continuous)
        let phonetic = data?["entry"]["phonetic"].text ?? ""
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
        .background(
            LinearGradient(colors: [Palette.paper, Palette.wash], startPoint: .topLeading, endPoint: .bottomTrailing),
            in: shape
        )
        .overlay(shape.stroke(back ? Palette.accent.opacity(0.65) : Palette.border, lineWidth: back ? 1.1 : 0.9))
        .shadow(color: Color(red: 0.06, green: 0.25, blue: 0.40).opacity(0.09), radius: 14, y: 5)
    }

    private func flip() {
        guard !flipping, !swiping, practice.current != nil else { return }
        if reduceMotion { revealed.toggle(); return }
        flipping = true
        withAnimation(.easeInOut(duration: 0.32)) { revealed.toggle() }
        flipTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 340_000_000)
            if !Task.isCancelled { flipping = false }
        }
    }

    private func answer(_ value: FlashcardPractice.Answer) {
        guard revealed, !flipping, !swiping else { return }
        if reduceMotion {
            practice.answer(value)
            persistProgress()
            revealed = false
            return
        }
        swiping = true
        let direction: CGFloat = value == .forgotten ? -1 : 1
        withAnimation(.easeIn(duration: 0.19)) {
            swipeOffset = direction * 110
            swipeAngle = Double(direction) * 6.5
            swipeOpacity = 0
        }
        flipTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 200_000_000)
            guard !Task.isCancelled else { return }
            practice.answer(value)
            persistProgress()
            revealed = false
            swipeOffset = 0
            swipeAngle = 0
            withAnimation(.spring(response: 0.28, dampingFraction: 0.85)) {
                swipeOpacity = 1
            }
            swiping = false
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
        resetSpellingInput()
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
