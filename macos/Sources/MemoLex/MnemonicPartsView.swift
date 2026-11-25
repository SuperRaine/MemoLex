import SwiftUI

struct MnemonicPartsView: View {
    let breakdowns: [J]

    var body: some View {
        ForEach(breakdowns.indices, id: \.self) { index in
            MnemonicBreakdownView(breakdown: breakdowns[index])
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
        let detail = selected.map { "\(parts[$0]["form"].text) · \(parts[$0]["meaning"].text)" } ?? breakdown["explanation"].text

        MorphemeFlowLayout(spacing: 7, lineSpacing: 8) {
            ForEach(parts.indices, id: \.self) { index in
                let part = parts[index]
                let form = part["form"].text
                let meaning = part["meaning"].text
                if index > 0 {
                    Text("＋")
                        .font(.system(size: 14, weight: .bold))
                        .foregroundStyle(Palette.accent.opacity(0.78))
                        .accessibilityHidden(true)
                }
                if meaning.isEmpty {
                    Text(form)
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(Palette.muted)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 6)
                        .background(Palette.paper, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.border, lineWidth: 1))
                } else {
                    MorphemeChipCard(
                        form: form,
                        meaning: meaning,
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
                .font(.system(size: 14, weight: .bold))
                .foregroundStyle(Palette.accent.opacity(0.78))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                if !word.isEmpty {
                    Text(word)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundStyle(Palette.ink)
                }
                Text(detail)
                    .font(.system(size: 12))
                    .foregroundStyle(Palette.deep)
                    .fixedSize(horizontal: false, vertical: true)
                    .id(detail)
                    .transition(.opacity)
                    .animation(reduceMotion ? nil : .easeOut(duration: 0.18), value: detail)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 7)
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
                    .strokeBorder(Palette.accent.opacity(0.38), lineWidth: 1)
            )
        }
        .padding(.vertical, 2)
    }
}

private struct MorphemeChipCard: View {
    let form: String
    let meaning: String
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
                    .font(.system(size: 14.5, weight: .bold))
                    .foregroundStyle(Palette.deep)
                Text(meaning)
                    .font(.system(size: 11.5))
                    .foregroundStyle(Palette.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 11)
            .padding(.vertical, 6)
            .background(highlighted ? Palette.wash.opacity(active ? 1.0 : 0.72) : Palette.paper, in: shape)
            .overlay(
                shape.strokeBorder(
                    active ? Palette.accent : (highlighted ? Palette.accent.opacity(0.72) : Palette.border),
                    lineWidth: highlighted ? 1.4 : 1.0
                )
            )
            .shadow(color: highlighted ? Palette.accent.opacity(active ? 0.24 : 0.14) : .clear, radius: active ? 8 : 5, y: 2)
            .opacity(highlighted ? 1.0 : 0.72)
            .scaleEffect(active && !reduceMotion ? 1.03 : 1)
            .offset(y: (hovered || active) && !reduceMotion ? -2 : 0)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: hovered)
            .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.78), value: active)
        }
        .buttonStyle(.plain)
        .onHover { hovered = $0 }
        .accessibilityLabel("\(form)，查看构词含义")
        .accessibilityAddTraits(active ? .isSelected : [])
        .help(meaning)
    }
}

private struct MorphemeFlowLayout: Layout {
    var spacing: CGFloat = 7
    var lineSpacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxWidth = proposal.width ?? .infinity
        var rows: [[CGSize]] = [[]]
        var rowWidth: CGFloat = 0

        for subview in subviews {
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
        var rows: [[(LayoutSubview, CGSize)]] = [[]]
        var rowWidth: CGFloat = 0

        for subview in subviews {
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
