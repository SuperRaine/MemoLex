import AppKit
import SwiftUI

struct MemoButtonStyle: ButtonStyle {
    var accent = false
    var compact = false
    @Environment(\.isEnabled) private var enabled
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: compact ? 11 : 13, weight: accent ? .semibold : .regular))
            .padding(.horizontal, compact ? 9 : 13).padding(.vertical, compact ? 6 : 9)
            .foregroundStyle(accent ? Palette.ink : Palette.deep)
            .background(accent ? Palette.accent.opacity(configuration.isPressed ? 0.72 : 1) : (configuration.isPressed ? Palette.wash : Color.white), in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(accent ? Palette.accent : Palette.border, lineWidth: 1))
            .opacity(enabled ? 1 : 0.38)
    }
}

struct MemoCheckStyle: ToggleStyle {
    @Environment(\.isEnabled) private var enabled
    func makeBody(configuration: Configuration) -> some View {
        Button { configuration.isOn.toggle() } label: {
            HStack(spacing: 8) {
                ZStack {
                    RoundedRectangle(cornerRadius: 5).fill(configuration.isOn ? Color(red: 0.18, green: 0.55, blue: 1) : Color.white)
                    RoundedRectangle(cornerRadius: 5).stroke(configuration.isOn ? Color.clear : Palette.border, lineWidth: 1)
                    if configuration.isOn { Image(systemName: "checkmark").font(.system(size: 11, weight: .bold)).foregroundStyle(.white) }
                }.frame(width: 18, height: 18)
                configuration.label
            }.frame(minHeight: 26, alignment: .leading).contentShape(Rectangle())
        }.buttonStyle(.plain).opacity(enabled ? 1 : 0.4)
            .accessibilityValue(Text(configuration.isOn ? "已选择" : "未选择"))
    }
}

struct Card<Content: View>: View {
    var padding: CGFloat = 20
    @ViewBuilder var content: Content
    var body: some View { content.padding(padding).frame(maxWidth: .infinity, alignment: .leading).background(Color.white, in: RoundedRectangle(cornerRadius: 13)).overlay(RoundedRectangle(cornerRadius: 13).stroke(Palette.border)) }
}

struct DialogHeader: View {
    var title: String
    var close: () -> Void
    var body: some View {
        HStack(spacing: 8) {
            OwlImage().frame(width: 27, height: 27)
            Text(title).font(.system(size: 13)).foregroundStyle(Palette.deep)
            Spacer()
            Button(action: close) { Image(systemName: "xmark").frame(width: 28, height: 28) }.buttonStyle(.plain).foregroundStyle(Palette.muted)
        }.padding(.horizontal, 13).frame(height: 44).background(.white).overlay(alignment: .bottom) { Divider().overlay(Palette.border) }
    }
}

struct OwlImage: View {
    private var image: NSImage? { NSImage(contentsOf: AppPaths.resources.appendingPathComponent("owl-logo.png")) }
    var body: some View { if let image { Image(nsImage: image).resizable().scaledToFit() } else { Image(systemName: "book.closed.fill").resizable().scaledToFit().foregroundStyle(Palette.deep) } }
}

struct LinkedText: View {
    var text: String
    var highlights: [String] = []
    var chineseHighlights: [String] = []
    var size: CGFloat = 15
    var color: Color = Palette.ink
    private var formatted: AttributedString {
        var value = AttributedString(text)
        value.foregroundColor = color
        let source = text as NSString
        let regex = try? NSRegularExpression(pattern: "[A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+(?:['’-][A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+)*")
        for match in regex?.matches(in: text, range: NSRange(location: 0, length: source.length)) ?? [] {
            guard let range = Range(match.range, in: text) else { continue }
            let token = String(text[range]); let prefix = text[..<range.lowerBound].count
            let start = value.characters.index(value.characters.startIndex, offsetBy: prefix)
            let end = value.characters.index(start, offsetBy: token.count)
            if let encoded = token.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed), let url = URL(string: "memolex://word/\(encoded)") { value[start..<end].link = url }
            if highlights.contains(where: { $0.caseInsensitiveCompare(token) == .orderedSame }) {
                value[start..<end].backgroundColor = Palette.wash; value[start..<end].foregroundColor = Palette.deep; value[start..<end].font = .system(size: size, weight: .semibold)
            }
        }
        for term in chineseHighlights where !term.isEmpty {
            var cursor = text.startIndex
            while cursor < text.endIndex, let range = text.range(of: term, range: cursor..<text.endIndex) {
                let start = value.characters.index(value.characters.startIndex, offsetBy: text[..<range.lowerBound].count)
                let end = value.characters.index(start, offsetBy: term.count)
                value[start..<end].backgroundColor = Palette.wash; value[start..<end].foregroundColor = Palette.deep
                cursor = range.upperBound
            }
        }
        return value
    }
    var body: some View { Text(formatted).font(.system(size: size)).lineSpacing(7).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading) }
}

extension View {
    func inputFrame(height: CGFloat = 36) -> some View { self.textFieldStyle(.plain).padding(.horizontal, 10).frame(height: height).background(.white, in: RoundedRectangle(cornerRadius: 9)).overlay(RoundedRectangle(cornerRadius: 9).stroke(Palette.border)) }
    func sectionLabel() -> some View { self.font(.system(size: 11, weight: .semibold)).foregroundStyle(Palette.muted) }
}
