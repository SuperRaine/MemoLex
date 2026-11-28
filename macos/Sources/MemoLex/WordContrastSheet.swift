import AppKit
import SwiftUI

struct WordContrastSheet: View {
    @ObservedObject var model: AppModel
    @State private var selectedWord: String? = nil

    private var report: J { model.contrastReport ?? .null }
    private var isSimilar: Bool { report["mode"].text == "similar" }
    private var modeTitle: String { isSimilar ? "形近区分" : "近义辨析" }
    private var targetWord: String { report["targetWord"].text }
    private var items: [J] { report["items"].list }

    private var visibleItems: [J] {
        guard let selected = selectedWord, !selected.isEmpty else { return items }
        return items.filter { $0["word"].text.caseInsensitiveCompare(selected) == .orderedSame }
    }

    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "\(modeTitle) · \(targetWord)") { model.sheet = nil }

            VStack(alignment: .leading, spacing: 10) {
                headerArea

                if items.count > 1 {
                    MorphemeFlowLayout(spacing: 6, lineSpacing: 5, expandLast: false) {
                        chipButton(label: "全部 (\(items.count))", value: nil)
                        ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                            let w = item["word"].text
                            chipButton(label: w, value: w)
                        }
                    }
                }

                ScrollView {
                    if items.isEmpty {
                        Text(isSimilar ? "暂未收录该词的形近词。" : "暂未收录该词的近义词。")
                            .font(.system(size: 13))
                            .foregroundStyle(Palette.muted)
                            .frame(maxWidth: .infinity)
                            .padding(.top, 42)
                    } else if isSimilar {
                        LazyVStack(alignment: .leading, spacing: 8) {
                            let targetSpans: [J] = {
                                if visibleItems.count == 1, !visibleItems[0]["targetSpans"].list.isEmpty {
                                    return visibleItems[0]["targetSpans"].list
                                }
                                let overview = report["targetOverviewSpans"].list
                                if !overview.isEmpty { return overview }
                                return [.object(["text": .string(targetWord), "highlight": .bool(false)])]
                            }()

                            similarMemoryRow(
                                word: targetWord,
                                spans: targetSpans,
                                phonetic: report["targetPhonetic"].text,
                                meaning: report["targetMeaning"].text,
                                memoryHook: report["targetMemoryHook"].text,
                                badge: "当前词",
                                isTarget: true,
                                isFocused: false
                            )

                            ForEach(Array(visibleItems.enumerated()), id: \.offset) { _, item in
                                let w = item["word"].text
                                let isFocused: Bool = {
                                    guard let focus = model.contrastFocusWord, !focus.isEmpty else { return false }
                                    return w.caseInsensitiveCompare(focus) == .orderedSame
                                }()
                                let spans = item["candidateSpans"].list.isEmpty
                                    ? [J.object(["text": .string(w), "highlight": .bool(false)])]
                                    : item["candidateSpans"].list
                                similarMemoryRow(
                                    word: w,
                                    spans: spans,
                                    phonetic: item["phonetic"].text,
                                    meaning: item["meaning"].text,
                                    memoryHook: item["memoryHook"].text,
                                    badge: isFocused ? "已定位" : nil,
                                    isTarget: false,
                                    isFocused: isFocused
                                )
                            }
                        }
                        .padding(.trailing, 4)
                    } else {
                        LazyVStack(alignment: .leading, spacing: 10) {
                            ForEach(Array(visibleItems.enumerated()), id: \.offset) { _, item in
                                synonymCard(item)
                            }
                        }
                        .padding(.trailing, 4)
                    }
                }
            }
            .padding(22)
        }
        .frame(width: 780, height: min(700, (NSScreen.main?.visibleFrame.height ?? 780) - 58))
        .background(Palette.paper)
        .foregroundStyle(Palette.ink)
        .font(.system(size: 13))
        .noFocusRing()
    }

    private var headerArea: some View {
        let anchor = report["groupAnchor"].text
        let phonetic = report["targetPhonetic"].text
        let meaning = report["targetMeaning"].text

        return VStack(alignment: .leading, spacing: 7) {
            HStack(spacing: 8) {
                Text("\(modeTitle) · \(targetWord)")
                    .font(.system(size: 19, weight: .bold))
                if !isSimilar && !phonetic.isEmpty {
                    Text(phonetic)
                        .font(.system(size: 12.5))
                        .foregroundStyle(Palette.muted)
                }
                if !isSimilar && !meaning.isEmpty {
                    Text(meaning)
                        .font(.system(size: 12.5))
                        .foregroundStyle(Palette.muted)
                        .lineLimit(1)
                }
                Spacer()
                if !isSimilar {
                    Button("英音") { model.speakWord(targetWord, accent: .british) }
                        .buttonStyle(PillButtonStyle())
                    Button("美音") { model.speakWord(targetWord, accent: .american) }
                        .buttonStyle(PillButtonStyle())
                }
            }

            if isSimilar && !anchor.isEmpty {
                Text(anchor)
                    .font(.system(size: 12.5, weight: .semibold))
                    .foregroundStyle(Palette.deep)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, 12).padding(.vertical, 7)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Palette.wash, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.accent.opacity(0.32), lineWidth: 0.9))
            } else if !isSimilar && !items.isEmpty {
                Text("共 \(items.count) 个近义词 · 对比词性边界、语义侧重与常见搭配（点击单词跳转查词）")
                    .font(.system(size: 12))
                    .foregroundStyle(Palette.muted)
            }
        }
    }

    private func chipButton(label: String, value: String?) -> some View {
        let active: Bool = {
            if let value, let selectedWord {
                return value.caseInsensitiveCompare(selectedWord) == .orderedSame
            }
            return value == nil && selectedWord == nil
        }()
        return Button(label) {
            selectedWord = value
        }
        .font(.system(size: 11.5, weight: active ? .semibold : .medium))
        .foregroundStyle(active ? Color.white : Palette.deep)
        .padding(.horizontal, 10).padding(.vertical, 4)
        .background(active ? Palette.accent : Palette.wash, in: Capsule())
        .overlay(Capsule().strokeBorder(active ? Palette.accent : Palette.accent.opacity(0.28), lineWidth: 0.8))
        .buttonStyle(.plain)
    }

    private func similarMemoryRow(
        word: String,
        spans: [J],
        phonetic: String,
        meaning: String,
        memoryHook: String,
        badge: String?,
        isTarget: Bool,
        isFocused: Bool
    ) -> some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack(alignment: .center, spacing: 8) {
                if let badge, !badge.isEmpty {
                    softPill(badge, accent: true)
                }

                if isTarget {
                    highlightedWord(spans: spans, isTarget: true)
                } else {
                    Button {
                        model.sheet = nil
                        model.lookup(word)
                    } label: {
                        highlightedWord(spans: spans, isTarget: false)
                    }
                    .buttonStyle(.plain)
                    .help("点击查词「\(word)」")
                }

                if !phonetic.isEmpty {
                    Text(phonetic)
                        .font(.system(size: 12))
                        .foregroundStyle(Palette.muted)
                }

                if !meaning.isEmpty {
                    Text(meaning)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundStyle(Palette.ink)
                        .lineLimit(2)
                        .fixedSize(horizontal: false, vertical: true)
                }

                Spacer(minLength: 4)

                if isTarget {
                    Button("英音") { model.speakWord(word, accent: .british) }
                        .buttonStyle(PillButtonStyle())
                    Button("美音") { model.speakWord(word, accent: .american) }
                        .buttonStyle(PillButtonStyle())
                } else {
                    Button("朗读") { model.speakWord(word, accent: .british) }
                        .buttonStyle(PillButtonStyle())
                        .help("朗读 \(word)")
                }
            }

            if !memoryHook.trimmed.isEmpty {
                HStack(alignment: .top, spacing: 7) {
                    statusPill(
                        "巧记",
                        bg: isTarget ? Palette.wash : Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.14),
                        border: isTarget ? Palette.accent.opacity(0.36) : Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.45),
                        fg: isTarget ? Palette.deep : Color(red: 0.70, green: 0.38, blue: 0.03)
                    )
                    Text(memoryHook)
                        .font(.system(size: 12.5))
                        .foregroundStyle(Palette.ink)
                        .lineSpacing(2.5)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 11)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(isTarget ? Palette.canvas : Palette.paper, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .strokeBorder(isTarget || isFocused ? Palette.accent : Palette.border, lineWidth: isTarget || isFocused ? 1.3 : 0.85)
        )
    }

    private func highlightedWord(spans: [J], isTarget: Bool) -> some View {
        let hlBg = isTarget
            ? Palette.accent.opacity(0.18)
            : Color(red: 0.90, green: 0.22, blue: 0.32).opacity(0.16)
        let hlFg = isTarget
            ? Palette.deep
            : Color(red: 0.76, green: 0.14, blue: 0.24)

        return HStack(spacing: 1.5) {
            ForEach(Array(spans.enumerated()), id: \.offset) { _, span in
                let text = span["text"].text
                let hl = span["highlight"].flag
                if hl {
                    Text(text)
                        .font(.system(size: 16.5, weight: .bold, design: .monospaced))
                        .foregroundStyle(hlFg)
                        .padding(.horizontal, 4).padding(.vertical, 1)
                        .background(hlBg, in: RoundedRectangle(cornerRadius: 4, style: .continuous))
                } else {
                    Text(text)
                        .font(.system(size: 16.5, weight: .bold, design: .monospaced))
                        .foregroundStyle(Palette.ink)
                }
            }
        }
    }

    private func synonymCard(_ item: J) -> some View {
        let word = item["word"].text
        let phonetic = item["phonetic"].text
        let posSummary = item["posSummary"].text
        let verdict = item["verdict"].text
        let shared = item["sharedSenses"].list.map(\.text).filter { !$0.isEmpty }
        let targetUnique = item["targetUniqueSenses"].list.map(\.text).filter { !$0.isEmpty }
        let candUnique = item["candidateUniqueSenses"].list.map(\.text).filter { !$0.isEmpty }
        let targetPhrases = item["targetPhrases"].list.map(\.text).filter { !$0.isEmpty }
        let candPhrases = item["candidatePhrases"].list.map(\.text).filter { !$0.isEmpty }
        let candMeaning = item["meaning"].text
        let candMnemonic = item["candidateMnemonic"].text
        let targetMeaning = report["targetMeaning"].text

        let isFocused: Bool = {
            guard let focus = model.contrastFocusWord, !focus.isEmpty else { return false }
            return word.caseInsensitiveCompare(focus) == .orderedSame
        }()
        let posDiff = posSummary.contains("不同")

        return VStack(alignment: .leading, spacing: 9) {
            HStack(spacing: 7) {
                Button(word) {
                    model.sheet = nil
                    model.lookup(word)
                }
                .buttonStyle(.plain)
                .font(.system(size: 16, weight: .bold))
                .foregroundStyle(Palette.deep)
                .help("点击查词「\(word)」")

                if !phonetic.isEmpty {
                    Text(phonetic)
                        .font(.system(size: 12))
                        .foregroundStyle(Palette.muted)
                }
                if isFocused {
                    softPill("已定位", accent: true)
                }
                Spacer()
                if !posSummary.isEmpty {
                    statusPill(
                        posSummary,
                        bg: posDiff ? Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.14) : Palette.wash,
                        border: posDiff ? Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.42) : Palette.accent.opacity(0.32),
                        fg: posDiff ? Color(red: 0.70, green: 0.38, blue: 0.03) : Palette.deep
                    )
                }
                Button("朗读") { model.speakWord(word, accent: .british) }
                    .buttonStyle(PillButtonStyle())
                    .help("朗读 \(word)")
            }

            if !verdict.isEmpty {
                Text(verdict)
                    .font(.system(size: 12.5))
                    .foregroundStyle(Palette.ink)
                    .lineSpacing(3)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, 11).padding(.vertical, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Palette.paper, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
            }

            if !shared.isEmpty {
                HStack(alignment: .center, spacing: 5) {
                    Text("共通义：")
                        .font(.system(size: 11.5, weight: .semibold))
                        .foregroundStyle(Palette.muted)
                    MorphemeFlowLayout(spacing: 5, lineSpacing: 4, expandLast: false) {
                        ForEach(Array(shared.enumerated()), id: \.offset) { _, sense in
                            statusPill(
                                sense,
                                bg: Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.13),
                                border: Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.42),
                                fg: Color(red: 0.05, green: 0.56, blue: 0.36)
                            )
                        }
                    }
                }
            }

            HStack(alignment: .top, spacing: 9) {
                sideColumn(
                    title: targetWord,
                    primary: targetUnique.isEmpty ? targetMeaning : "侧重：\(targetUnique.joined(separator: "、"))",
                    fullMeaning: targetMeaning,
                    phrases: targetPhrases,
                    mnemonic: "",
                    isTarget: true
                )
                sideColumn(
                    title: word,
                    primary: candUnique.isEmpty ? candMeaning : "侧重：\(candUnique.joined(separator: "、"))",
                    fullMeaning: candMeaning,
                    phrases: candPhrases,
                    mnemonic: candMnemonic,
                    isTarget: false
                )
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 11, style: .continuous)
                .strokeBorder(isFocused ? Palette.accent : Palette.border, lineWidth: isFocused ? 1.3 : 0.85)
        )
    }

    private func sideColumn(
        title: String,
        primary: String,
        fullMeaning: String?,
        phrases: [String],
        mnemonic: String,
        isTarget: Bool
    ) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.system(size: 13, weight: .bold))
                .foregroundStyle(isTarget ? Palette.deep : Palette.ink)
            if !primary.isEmpty {
                Text(primary)
                    .font(.system(size: 12, weight: primary.hasPrefix("侧重：") ? .semibold : .regular))
                    .foregroundStyle(Palette.ink)
                    .lineSpacing(2)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let full = fullMeaning, !full.isEmpty, full != primary {
                Text(full)
                    .font(.system(size: 11.5))
                    .foregroundStyle(Palette.muted)
                    .lineSpacing(2)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if !phrases.isEmpty {
                MorphemeFlowLayout(spacing: 5, lineSpacing: 4, expandLast: false) {
                    ForEach(Array(phrases.enumerated()), id: \.offset) { _, p in
                        softPill(p, accent: false)
                    }
                }
                .padding(.top, 2)
            }
            if !mnemonic.trimmed.isEmpty {
                Text("助记：\(mnemonic)")
                    .font(.system(size: 11))
                    .foregroundStyle(Palette.muted)
                    .lineSpacing(2)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 2)
            }
        }
        .padding(.horizontal, 10).padding(.vertical, 8)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(Palette.paper, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
    }

    private func softPill(_ text: String, accent: Bool) -> some View {
        Text(text)
            .font(.system(size: 10.5, weight: .semibold))
            .foregroundStyle(accent ? Palette.deep : Palette.muted)
            .padding(.horizontal, 7).padding(.vertical, 2)
            .background(accent ? Palette.wash : Palette.paper, in: Capsule())
            .overlay(Capsule().strokeBorder(accent ? Palette.accent.opacity(0.32) : Palette.border, lineWidth: 0.8))
    }

    private func statusPill(_ text: String, bg: Color, border: Color, fg: Color) -> some View {
        Text(text)
            .font(.system(size: 10.5, weight: .semibold))
            .foregroundStyle(fg)
            .padding(.horizontal, 7).padding(.vertical, 2)
            .background(bg, in: Capsule())
            .overlay(Capsule().strokeBorder(border, lineWidth: 0.8))
    }
}
