import AppKit
import SwiftUI

struct MemoButtonStyle: ButtonStyle {
    var accent = false
    var compact = false
    @Environment(\.isEnabled) private var enabled
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(compact ? .callout : .body)
            .padding(.horizontal, compact ? 8 : 12).padding(.vertical, compact ? 4 : 6)
            .foregroundStyle(accent ? Color.white : Palette.ink)
            .background(accent ? Palette.accent.opacity(configuration.isPressed ? 0.78 : 1) :
                Color(nsColor: .controlBackgroundColor).opacity(configuration.isPressed ? 0.5 : 1),
                in: RoundedRectangle(cornerRadius: 6))
            .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(accent ? Color.clear : Palette.border, lineWidth: 0.6))
            .opacity(enabled ? 1 : 0.38)
    }
}

struct Card<Content: View>: View {
    var padding: CGFloat = 20
    @ViewBuilder var content: Content
    var body: some View { content.padding(padding).frame(maxWidth: .infinity, alignment: .leading).background(Palette.paper, in: RoundedRectangle(cornerRadius: 12)).overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Palette.border, lineWidth: 0.5)) }
}

struct DialogHeader: View {
    var title: String
    var close: () -> Void
    var body: some View {
        HStack(spacing: 8) {
            OwlImage().frame(width: 27, height: 27)
            Text(title).font(.headline)
            Spacer()
            Button(action: close) { Image(systemName: "xmark.circle.fill").font(.system(size: 17)).frame(width: 28, height: 28) }
                .buttonStyle(.borderless).foregroundStyle(.tertiary).help("关闭").accessibilityLabel("关闭")
        }.padding(.horizontal, 13).frame(height: 45).background(.bar).overlay(alignment: .bottom) { Divider() }
    }
}

struct OwlImage: View {
    private static let image = NSImage(contentsOf: AppPaths.resources.appendingPathComponent("owl-logo.png"))
    var body: some View { if let image = Self.image { Image(nsImage: image).resizable().scaledToFit() } else { Image(systemName: "book.closed.fill").resizable().scaledToFit().foregroundStyle(Palette.deep) } }
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
    func inputFrame(height: CGFloat = 36) -> some View { self.textFieldStyle(.plain).padding(.horizontal, 10).frame(height: height).background(Palette.paper, in: RoundedRectangle(cornerRadius: 7)).overlay(RoundedRectangle(cornerRadius: 7).strokeBorder(Palette.border, lineWidth: 0.6)) }
    func sectionLabel() -> some View { self.font(.system(size: 11, weight: .semibold)).foregroundStyle(Palette.muted) }
}
