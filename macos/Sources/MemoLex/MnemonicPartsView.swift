import SwiftUI

struct MnemonicPartsView: View {
    let breakdowns: [J]

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ForEach(breakdowns.indices, id: \.self) { index in
                MnemonicBreakdownView(breakdown: breakdowns[index])
            }
        }
    }
}

private struct MnemonicBreakdownView: View {
    let breakdown: J
    @State private var selected: Int?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let parts = breakdown["parts"].list
        let word = breakdown["word"].text
        let detail: String = {
            if let index = selected, parts.indices.contains(index) {
                let focus = parts[index]["focusExplanation"].text
                if !focus.isEmpty { return focus }
                return "\(parts[index]["form"].text) · \(parts[index]["meaning"].text)"
            }
            return breakdown["explanation"].text
        }()

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
                        lit: selected == nil || selected == index,
                        active: selected == index,
                        reduceMotion: reduceMotion
                    ) {
                        withAnimation(reduceMotion ? nil : .spring(response: 0.26, dampingFraction: 0.78)) {
                            selected = selected == index ? nil : index
                        }
                    }
                }
            }
            Text("→")
                .font(.system(size: 14.5, weight: .bold))
                .foregroundStyle(Palette.accent.opacity(0.82))
                .accessibilityHidden(true)
            Button {
                if selected != nil {
                    withAnimation(reduceMotion ? nil : .spring(response: 0.26, dampingFraction: 0.78)) {
                        selected = nil
                    }
                }
            } label: {
                VStack(alignment: .leading, spacing: 2) {
                    if !word.isEmpty {
                        Text(word)
                            .font(.system(size: 14.5, weight: .bold))
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
                .padding(.horizontal, 13)
                .padding(.vertical, 8)
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
            .buttonStyle(.plain)
            .help("点击还原完整构词解释")
        }
        .padding(.vertical, 2)
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
        let shape = RoundedRectangle(cornerRadius: 10, style: .continuous)
        Button(action: action) {
            VStack(alignment: .leading, spacing: 2) {
                Text(form)
                    .font(.system(size: 15, weight: .bold))
                    .foregroundStyle(Palette.deep)
                Text(meaning)
                    .font(.system(size: 11.5))
                    .foregroundStyle(Palette.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 11)
            .padding(.vertical, 7)
            .background(highlighted ? Palette.wash.opacity(active ? 1.0 : 0.78) : Palette.paper, in: shape)
            .overlay(
                shape.strokeBorder(
                    active ? Palette.accent : (highlighted ? Palette.accent.opacity(0.78) : Palette.border),
                    lineWidth: 1.5
                )
            )
            .shadow(color: highlighted ? Palette.accent.opacity(active ? 0.22 : 0.13) : .clear, radius: active ? 9 : 6, y: 3)
            .scaleEffect(active && !reduceMotion ? 1.03 : 1)
            .offset(y: (highlighted) && !reduceMotion ? -1.5 : 0)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: hovered)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: active)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: lit)
        }
        .buttonStyle(.plain)
        .onHover { hovered = $0 }
        .accessibilityLabel("\(form)，点击查看构词含义")
        .accessibilityAddTraits(active ? .isSelected : [])
        .help(tooltip)
    }
}

private struct MorphemeFlowLayout: Layout {
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
