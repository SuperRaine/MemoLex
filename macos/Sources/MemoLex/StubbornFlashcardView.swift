import SwiftUI

struct StubbornFlashcardView: View {
    let core: CoreBridge
    let close: () -> Void
    @State private var practice: FlashcardPractice
    @State private var revealed = false
    @State private var turn = 0.0
    @State private var flipping = false
    @State private var flipTask: Task<Void, Never>?
    @State private var lookup: (word: String, data: J)?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(core: CoreBridge, words: [String], close: @escaping () -> Void) {
        self.core = core
        self.close = close
        _practice = State(initialValue: FlashcardPractice(words: words))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 15) {
            HStack {
                VStack(alignment: .leading, spacing: 5) {
                    Text("卡片自测").font(.system(size: 24, weight: .semibold))
                    Text("第 \(practice.groupNumber) / \(practice.groupCount) 组 · 剩余 \(practice.queue.count) / \(practice.groupWordCount) 词")
                        .foregroundStyle(Palette.muted)
                }
                Spacer()
                Button("返回列表", action: close).buttonStyle(MemoButtonStyle(compact: true))
            }
            if let word = practice.current {
                let data = lookup?.word == word ? lookup?.data : nil
                Button(action: flip) {
                    VStack(spacing: 10) {
                        Text(word).font(.system(size: 36, weight: .semibold)).multilineTextAlignment(.center)
                        if let phonetic = data?["entry"]["phonetic"].text, !phonetic.isEmpty {
                            Text(phonetic).foregroundStyle(Palette.muted)
                        }
                        if revealed {
                            WordListMeaning(core: core, word: word, local: data?["entry"]["translation"].text) { _ in }.id(word)
                            if let note = data?["notes"].list.first?["text"].text, !note.isEmpty {
                                Text(note).font(.caption).foregroundStyle(Palette.muted).lineLimit(3).frame(maxWidth: .infinity, alignment: .leading)
                            }
                        } else {
                            Label("点击翻卡核对释义", systemImage: "arrow.clockwise")
                                .foregroundStyle(Palette.muted).font(.callout)
                        }
                    }.padding(24).frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Palette.wash, in: RoundedRectangle(cornerRadius: 15))
                        .overlay(RoundedRectangle(cornerRadius: 15).stroke(Palette.border))
                }.buttonStyle(.plain)
                    .rotation3DEffect(.degrees(turn), axis: (x: 0, y: 1, z: 0))
                    .keyboardShortcut(.space, modifiers: [])
                    .accessibilityLabel(revealed ? "隐藏释义" : "翻卡查看释义")
                HStack(spacing: 9) {
                    Spacer()
                    Button("认识") { answer(.known) }.buttonStyle(MemoButtonStyle(accent: true)).keyboardShortcut("1", modifiers: [])
                    Button("模糊") { answer(.vague) }.buttonStyle(MemoButtonStyle()).keyboardShortcut("2", modifiers: [])
                    Button("忘记") { answer(.forgotten) }.buttonStyle(MemoButtonStyle()).keyboardShortcut("3", modifiers: [])
                }.disabled(!revealed || flipping)
            } else {
                VStack(spacing: 13) {
                    Image(systemName: "checkmark.circle").font(.system(size: 32)).foregroundStyle(Palette.deep)
                    Text(practice.hasNextGroup ? "本组已完成" : "全部练习完成").font(.title2.weight(.semibold))
                    Button(practice.hasNextGroup ? "下一组" : "完成") {
                        if !practice.startNextGroup() { close() }
                    }.buttonStyle(MemoButtonStyle(accent: true))
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            Text("练习结果仅记录在本组自测中").font(.footnote).foregroundStyle(Palette.muted)
        }.task(id: practice.current) {
            guard let word = practice.current else { return }
            let result = try? await core.request("dictionary.lookup", ["word": .string(word)])
            guard !Task.isCancelled, practice.current == word else { return }
            lookup = (word, result ?? .null)
        }.onDisappear { flipTask?.cancel() }
    }

    private func flip() {
        guard !flipping, practice.current != nil else { return }
        if reduceMotion { revealed.toggle(); return }
        flipping = true
        withAnimation(.easeIn(duration: 0.11)) { turn = 90 }
        flipTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 110_000_000)
            guard !Task.isCancelled else { return }
            revealed.toggle()
            turn = -90
            withAnimation(.easeOut(duration: 0.15)) { turn = 0 }
            try? await Task.sleep(nanoseconds: 150_000_000)
            if !Task.isCancelled { flipping = false }
        }
    }

    private func answer(_ value: FlashcardPractice.Answer) {
        guard revealed, !flipping else { return }
        practice.answer(value)
        revealed = false
    }
}
