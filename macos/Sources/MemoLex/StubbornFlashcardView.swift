import SwiftUI

struct StubbornFlashcardView: View {
    let core: CoreBridge
    let freshWords: [String]
    let persistsProgress: Bool
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
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(
        core: CoreBridge,
        words: [String],
        savedProgress: FlashcardProgressSnapshot? = nil,
        persistsProgress: Bool = true,
        onSaveProgress: @escaping (FlashcardProgressSnapshot) -> Void = { _ in },
        onClearProgress: @escaping () -> Void = {},
        close: @escaping () -> Void
    ) {
        self.core = core
        self.freshWords = words
        self.persistsProgress = persistsProgress
        self.onSaveProgress = onSaveProgress
        self.onClearProgress = onClearProgress
        self.close = close
        _practice = State(initialValue: FlashcardPractice(words: words, saved: persistsProgress ? savedProgress : nil))
    }

    private var groupProgress: CGFloat {
        let total = max(1, practice.groupWordCount)
        return CGFloat(max(0, total - practice.queue.count)) / CGFloat(total)
    }

    private var canReset: Bool {
        practice.hasProgress || (practice.current == nil && !practice.hasNextGroup)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                VStack(alignment: .leading, spacing: 5) {
                    Text("卡片自测").font(.system(size: 24, weight: .semibold))
                    Text("第 \(practice.groupNumber) / \(practice.groupCount) 组 · 剩余 \(practice.queue.count) / \(practice.groupWordCount) 词 · 已掌握 \(practice.masteredCount) / \(practice.totalWordCount)")
                        .foregroundStyle(Palette.muted)
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

                    Button(action: flip) {
                        let angle = revealed ? 180.0 : 0
                        ZStack {
                            face(word: word, data: data, back: false)
                                .modifier(FlashcardFaceTurn(angle: angle, back: false))
                                .accessibilityHidden(revealed)
                            face(word: word, data: data, back: true)
                                .modifier(FlashcardFaceTurn(angle: angle, back: true))
                                .accessibilityHidden(!revealed)
                        }
                        .contentShape(RoundedRectangle(cornerRadius: 15, style: .continuous))
                    }
                    .buttonStyle(.plain)
                    .offset(x: swipeOffset)
                    .rotationEffect(.degrees(swipeAngle))
                    .opacity(swipeOpacity)
                    .accessibilityLabel(revealed ? "隐藏释义" : "翻卡查看释义")
                    .keyboardShortcut(.space, modifiers: [])
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
                        Text("先点击卡片翻面核对释义（空格键），再选择掌握程度")
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
        }.task(id: practice.current) {
            guard let word = practice.current else { return }
            let result = try? await core.request("dictionary.lookup", ["word": .string(word)])
            guard !Task.isCancelled, practice.current == word else { return }
            lookup = (word, result ?? .null)
        }.onDisappear { flipTask?.cancel() }
    }

    private func face(word: String, data: J?, back: Bool) -> some View {
        let shape = RoundedRectangle(cornerRadius: 15, style: .continuous)
        return VStack(spacing: 10) {
            Text(word).font(.system(size: 36, weight: .semibold)).multilineTextAlignment(.center)
            if let phonetic = data?["entry"]["phonetic"].text, !phonetic.isEmpty {
                Text(phonetic).foregroundStyle(Palette.muted)
            }
            if back {
                ScrollView {
                    VStack(alignment: .leading, spacing: 7) {
                        Text("中文释义").font(.caption.weight(.semibold)).foregroundStyle(Palette.deep)
                        WordListMeaning(core: core, word: word, local: data?["entry"]["translation"].text, concealed: !revealed) { _ in }.id(word)
                        if let note = data?["notes"].list.first?["text"].text, !note.isEmpty {
                            VStack(alignment: .leading, spacing: 3) {
                                Text("助记").font(.caption.weight(.semibold)).foregroundStyle(Palette.deep)
                                Text(note).font(.callout).foregroundStyle(Palette.ink.opacity(0.85)).fixedSize(horizontal: false, vertical: true)
                            }
                            .padding(10)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Palette.paper.opacity(0.82), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                            .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).stroke(Palette.border.opacity(0.75)))
                            .padding(.top, 4)
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading)
                }
            } else {
                Label("点击翻卡核对释义", systemImage: "arrow.clockwise")
                    .font(.callout.weight(.medium))
                    .foregroundStyle(Palette.deep)
                    .padding(.horizontal, 12).padding(.vertical, 5)
                    .background(Palette.paper.opacity(0.85), in: Capsule())
                    .padding(.top, 6)
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
