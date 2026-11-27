import SwiftUI

struct MnemonicPartsView: View {
    let breakdowns: [J]
    var lookupWord: ((String) -> Void)? = nil
    var loadFamily: ((String, String) async -> [J])? = nil
    var practiceGroup: ((String, String) -> Void)? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ForEach(breakdowns.indices, id: \.self) { index in
                MnemonicBreakdownView(
                    breakdown: breakdowns[index],
                    lookupWord: lookupWord,
                    loadFamily: loadFamily,
                    practiceGroup: practiceGroup
                )
            }
        }
    }
}

private struct MnemonicBreakdownView: View {
    let breakdown: J
    let lookupWord: ((String) -> Void)?
    let loadFamily: ((String, String) async -> [J])?
    let practiceGroup: ((String, String) -> Void)?
    @State private var selected: Set<Int> = []
    @State private var familyKey: String? = nil
    @State private var familyTitle = ""
    @State private var familyPart: (form: String, meaning: String)? = nil
    @State private var familyMembers: [J] = []
    @State private var loadingFamily = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let parts = breakdown["parts"].list
        let word = breakdown["word"].text
        let clickableIndices = Set(parts.indices.filter { !parts[$0]["meaning"].text.isEmpty })
        let allLit = selected.isEmpty || selected.count >= clickableIndices.count
        let chosenParts: [J] = allLit ? [] : selected.sorted().compactMap { parts.indices.contains($0) ? parts[$0] : nil }
        let detail: String = {
            if allLit {
                return breakdown["explanation"].text
            }
            let lines = chosenParts.map { part -> String in
                let focus = part["focusExplanation"].text
                if !focus.isEmpty { return focus }
                return "\(part["form"].text) · \(part["meaning"].text)"
            }
            return lines.isEmpty ? breakdown["explanation"].text : lines.joined(separator: "\n")
        }()
        let cognateExamples: [String] = {
            var seen = Set<String>()
            var result: [String] = []
            for part in chosenParts {
                for item in part["examples"].list {
                    let text = item.text
                    if !text.isEmpty, seen.insert(text.lowercased()).inserted {
                        result.append(text)
                    }
                }
            }
            return Array(result.prefix(6))
        }()

        VStack(alignment: .leading, spacing: 8) {
            MorphemeFlowLayout(spacing: 7, lineSpacing: 8, expandLast: true, lastMinWidth: 185) {
                ForEach(parts.indices, id: \.self) { index in
                    let part = parts[index]
                    let form = part["form"].text
                    let meaning = part["meaning"].text
                    let focus = part["focusExplanation"].text
                    if index > 0 {
                        Text("＋")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundStyle(Palette.accent.opacity(0.82))
                            .accessibilityHidden(true)
                    }
                    if meaning.isEmpty {
                        Text(form)
                            .font(.system(size: 14.5, weight: .bold))
                            .foregroundStyle(Palette.muted)
                            .padding(.horizontal, 11)
                            .padding(.vertical, 7)
                            .background(Palette.paper, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.border, lineWidth: 1.5))
                    } else {
                        MorphemeChipCard(
                            form: form,
                            meaning: meaning,
                            tooltip: focus.isEmpty ? meaning : focus,
                            lit: allLit || selected.contains(index),
                            active: !allLit && selected.contains(index),
                            reduceMotion: reduceMotion
                        ) {
                            withAnimation(reduceMotion ? nil : .spring(response: 0.26, dampingFraction: 0.78)) {
                                familyKey = nil
                                familyMembers = []
                                if allLit {
                                    selected = [index]
                                } else if selected.contains(index) {
                                    if selected.count == 1 {
                                        selected.removeAll()
                                    } else {
                                        selected.remove(index)
                                    }
                                } else {
                                    selected.insert(index)
                                    if selected.count >= clickableIndices.count {
                                        selected.removeAll()
                                    }
                                }
                            }
                        }
                    }
                }
                Text("→")
                    .font(.system(size: 14.5, weight: .bold))
                    .foregroundStyle(Palette.accent.opacity(0.82))
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 6) {
                    Button {
                        if !selected.isEmpty {
                            withAnimation(reduceMotion ? nil : .spring(response: 0.26, dampingFraction: 0.78)) {
                                selected.removeAll()
                                familyKey = nil
                                familyMembers = []
                            }
                        }
                    } label: {
                        VStack(alignment: .leading, spacing: 1) {
                            if !word.isEmpty {
                                Text(word)
                                    .font(.system(size: 13.5, weight: .bold))
                                    .foregroundStyle(Palette.ink)
                            }
                            Text(detail)
                                .font(.system(size: 12.5))
                                .foregroundStyle(Palette.deep)
                                .multilineTextAlignment(.leading)
                                .fixedSize(horizontal: false, vertical: true)
                                .id(detail)
                                .transition(.opacity)
                                .animation(reduceMotion ? nil : .easeOut(duration: 0.18), value: detail)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .buttonStyle(.plain)
                    .help("点击还原完整构词解释")

                    if !allLit && (lookupWord != nil) && (!cognateExamples.isEmpty || (chosenParts.count == 1 && loadFamily != nil)) {
                        MorphemeFlowLayout(spacing: 5, lineSpacing: 5, expandLast: false) {
                            if !cognateExamples.isEmpty {
                                Text("同根词：")
                                    .font(.system(size: 11))
                                    .foregroundStyle(Palette.muted)
                                ForEach(cognateExamples, id: \.self) { ex in
                                    Button(ex) { lookupWord?(ex) }
                                        .buttonStyle(PillButtonStyle())
                                        .help("点击查询「\(ex)」")
                                }
                            }
                            if chosenParts.count == 1, let loadFamily {
                                let part = chosenParts[0]
                                let form = part["form"].text
                                let meaning = part["meaning"].text
                                let key = "\(form)|\(meaning)"
                                Button(familyKey == key ? "收起家族" : (loadingFamily ? "加载中…" : "同根词家族 →")) {
                                    if familyKey == key {
                                        familyKey = nil
                                        familyPart = nil
                                        familyMembers = []
                                    } else {
                                        loadingFamily = true
                                        Task {
                                            let items = await loadFamily(form, meaning)
                                            loadingFamily = false
                                            if !items.isEmpty {
                                                familyTitle = "「\(form)（\(meaning)）」同根词家族 · 共 \(items.count) 词"
                                                familyPart = (form, meaning)
                                                familyMembers = items
                                                familyKey = key
                                            }
                                        }
                                    }
                                }
                                .buttonStyle(PillButtonStyle())
                                .disabled(loadingFamily)
                                if let practiceGroup {
                                    Button("练这组") { practiceGroup(form, meaning) }
                                        .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                                        .help("把这组同根词放在一起对比词缀并做辨析小测")
                                }
                            }
                        }
                    }
                }
                .padding(.horizontal, 11)
                .padding(.vertical, 6)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(
                    LinearGradient(
                        colors: [Palette.wash, Palette.wash.opacity(0.55)],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing
                    ),
                    in: RoundedRectangle(cornerRadius: 10, style: .continuous)
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .strokeBorder(Palette.accent.opacity(0.42), lineWidth: 1)
                )
            }
            .padding(.vertical, 2)

            if familyKey != nil, !familyMembers.isEmpty {
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text(familyTitle)
                            .font(.system(size: 11.5, weight: .semibold))
                            .foregroundStyle(Palette.deep)
                        Spacer()
                        if let practiceGroup, let part = familyPart {
                            Button("练这组") { practiceGroup(part.form, part.meaning) }
                                .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                                .help("把这组同根词放在一起对比词缀并做辨析小测")
                        }
                    }
                    MorphemeFlowLayout(spacing: 7, lineSpacing: 7, expandLast: false) {
                        ForEach(Array(familyMembers.enumerated()), id: \.offset) { _, member in
                            let memberWord = member["word"].text
                            let memberMeaning = member["meaning"].text
                            Button { lookupWord?(memberWord) } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(memberWord)
                                        .font(.system(size: 13, weight: .bold))
                                        .foregroundStyle(Palette.deep)
                                    Text(memberMeaning)
                                        .font(.system(size: 11))
                                        .foregroundStyle(Palette.muted)
                                        .lineLimit(1)
                                }
                                .frame(maxWidth: 168, alignment: .leading)
                                .padding(.horizontal, 9).padding(.vertical, 6)
                                .background(Palette.paper, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85))
                            }
                            .buttonStyle(.plain)
                            .noFocusRing()
                            .help("\(memberWord)\n\(memberMeaning)")
                        }
                    }
                }
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.paper.opacity(0.9), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.9))
            }
        }
    }
}

private struct MorphemeChipCard: View {
    let form: String
    let meaning: String
    let tooltip: String
    let lit: Bool
    let active: Bool
    let reduceMotion: Bool
    let action: () -> Void
    @State private var hovered = false

    var body: some View {
        let highlighted = lit || hovered
        let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
        Button(action: action) {
            VStack(alignment: .leading, spacing: 1.5) {
                Text(form)
                    .font(.system(size: 14, weight: .bold))
                    .foregroundStyle(Palette.deep)
                Text(meaning)
                    .font(.system(size: 11))
                    .foregroundStyle(Palette.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 9)
            .padding(.vertical, 5)
            .background(highlighted ? Palette.wash.opacity(active ? 1.0 : 0.78) : Palette.paper, in: shape)
            .overlay(
                shape.strokeBorder(
                    active ? Palette.accent : (highlighted ? Palette.accent.opacity(0.78) : Palette.border),
                    lineWidth: 1.3
                )
            )
            .shadow(color: highlighted ? Palette.accent.opacity(active ? 0.20 : 0.11) : .clear, radius: active ? 7 : 4, y: 2)
            .scaleEffect(active && !reduceMotion ? 1.03 : 1)
            .offset(y: (highlighted) && !reduceMotion ? -1.5 : 0)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: hovered)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: active)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: lit)
        }
        .buttonStyle(.plain)
        .noFocusRing()
        .onHover { hovered = $0 }
        .accessibilityLabel("\(form)，点击查看构词含义")
        .accessibilityAddTraits(active ? .isSelected : [])
        .help(tooltip)
    }
}

struct MorphemeFlowLayout: Layout {
    var spacing: CGFloat = 7
    var lineSpacing: CGFloat = 8
    var expandLast: Bool = true
    var lastMinWidth: CGFloat = 185

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxWidth = proposal.width ?? .infinity
        let count = subviews.count
        var rows: [[CGSize]] = [[]]
        var rowWidth: CGFloat = 0

        for (idx, subview) in subviews.enumerated() {
            let isLast = expandLast && idx == count - 1
            if isLast && maxWidth.isFinite {
                let remaining = rows[rows.count - 1].isEmpty ? maxWidth : maxWidth - rowWidth - spacing
                let targetWidth: CGFloat
                if !rows[rows.count - 1].isEmpty && remaining < lastMinWidth {
                    rows.append([])
                    rowWidth = 0
                    targetWidth = maxWidth
                } else {
                    targetWidth = max(1, remaining)
                }
                let size = subview.sizeThatFits(ProposedViewSize(width: targetWidth, height: nil))
                if !rows[rows.count - 1].isEmpty { rowWidth += spacing }
                rows[rows.count - 1].append(CGSize(width: targetWidth, height: size.height))
                rowWidth += targetWidth
            } else {
                let size = subview.sizeThatFits(ProposedViewSize(width: maxWidth.isFinite ? maxWidth : nil, height: nil))
                if !rows[rows.count - 1].isEmpty && rowWidth + spacing + size.width > maxWidth {
                    rows.append([size])
                    rowWidth = size.width
                } else {
                    if !rows[rows.count - 1].isEmpty { rowWidth += spacing }
                    rows[rows.count - 1].append(size)
                    rowWidth += size.width
                }
            }
        }

        var totalHeight: CGFloat = 0
        var widest: CGFloat = 0
        for (idx, row) in rows.enumerated() {
            let width = row.reduce(0) { $0 + $1.width } + CGFloat(max(0, row.count - 1)) * spacing
            let height = row.map(\.height).max() ?? 0
            widest = max(widest, width)
            if idx > 0 { totalHeight += lineSpacing }
            totalHeight += height
        }
        return CGSize(width: maxWidth.isFinite ? maxWidth : widest, height: totalHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let maxWidth = bounds.width
        let count = subviews.count
        var rows: [[(LayoutSubview, CGSize)]] = [[]]
        var rowWidth: CGFloat = 0

        for (idx, subview) in subviews.enumerated() {
            let isLast = expandLast && idx == count - 1
            if isLast {
                let remaining = rows[rows.count - 1].isEmpty ? maxWidth : maxWidth - rowWidth - spacing
                let targetWidth: CGFloat
                if !rows[rows.count - 1].isEmpty && remaining < lastMinWidth {
                    rows.append([])
                    rowWidth = 0
                    targetWidth = maxWidth
                } else {
                    targetWidth = max(1, remaining)
                }
                let size = subview.sizeThatFits(ProposedViewSize(width: targetWidth, height: nil))
                if !rows[rows.count - 1].isEmpty { rowWidth += spacing }
                rows[rows.count - 1].append((subview, CGSize(width: targetWidth, height: size.height)))
                rowWidth += targetWidth
            } else {
                let size = subview.sizeThatFits(ProposedViewSize(width: maxWidth, height: nil))
                if !rows[rows.count - 1].isEmpty && rowWidth + spacing + size.width > maxWidth {
                    rows.append([(subview, size)])
                    rowWidth = size.width
                } else {
                    if !rows[rows.count - 1].isEmpty { rowWidth += spacing }
                    rows[rows.count - 1].append((subview, size))
                    rowWidth += size.width
                }
            }
        }

        var y = bounds.minY
        for row in rows {
            let rowHeight = row.map(\.1.height).max() ?? 0
            var x = bounds.minX
            for (subview, size) in row {
                let itemY = y + (rowHeight - size.height) / 2
                subview.place(
                    at: CGPoint(x: x, y: itemY),
                    proposal: ProposedViewSize(width: min(size.width, maxWidth), height: size.height)
                )
                x += size.width + spacing
            }
            y += rowHeight + lineSpacing
        }
    }
}
