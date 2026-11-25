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

    var body: some View {
        let parts = breakdown["parts"].list
        VStack(alignment: .leading, spacing: 7) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 6) {
                    ForEach(parts.indices, id: \.self) { index in
                        let part = parts[index]
                        let form = part["form"].text
                        let meaning = part["meaning"].text
                        if index > 0 { Text("＋").foregroundStyle(Palette.muted).accessibilityHidden(true) }
                        if meaning.isEmpty { Text(form).foregroundStyle(Palette.muted) }
                        else {
                            Button {
                                selected = selected == index ? nil : index
                            } label: {
                                Text(form).font(.system(size: 14, weight: .semibold))
                                    .padding(.horizontal, 10).padding(.vertical, 6)
                                    .foregroundStyle(Palette.deep)
                                    .background(selected == nil || selected == index ? Palette.accent.opacity(0.22) : Palette.paper, in: RoundedRectangle(cornerRadius: 6))
                            }.buttonStyle(.plain)
                                .accessibilityLabel("\(form)，查看构词含义")
                                .accessibilityAddTraits(selected == index ? .isSelected : [])
                                .help(meaning)
                        }
                    }
                    let word = breakdown["word"].text
                    if !word.isEmpty { Text("→ \(word)").fontWeight(.semibold).foregroundStyle(Palette.ink) }
                }
            }
            let detail = selected.map { "\(parts[$0]["form"].text) · \(parts[$0]["meaning"].text)" } ?? breakdown["explanation"].text
            Text(detail).font(.system(size: 12)).foregroundStyle(Palette.muted)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
