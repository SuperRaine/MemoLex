import AppKit
import SwiftUI

struct MemoButtonStyle: ButtonStyle {
    var accent = false
    var compact = false
    @Environment(\.isEnabled) private var enabled
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: compact ? 12 : 13, weight: accent ? .semibold : .medium))
            .padding(.horizontal, compact ? 9 : 13)
            .frame(minHeight: compact ? 27 : 33)
            .foregroundStyle(accent ? Palette.accentInk : Palette.ink)
            .background(accent ? Palette.accent.opacity(configuration.isPressed ? 0.78 : 1) :
                (configuration.isPressed ? Palette.wash : Palette.control),
                in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(accent ? Color.clear : Palette.border, lineWidth: 0.65))
            .opacity(enabled ? 1 : 0.38)
    }
}

struct MemoPicker<Value: Hashable>: View {
    var title: String
    @Binding var selection: Value
    var options: [(Value, String)]
    var placeholder: String? = nil
    var height: CGFloat = 34
    @Environment(\.isEnabled) private var enabled

    private var selectedLabel: String { options.first { $0.0 == selection }?.1 ?? placeholder ?? title }

    var body: some View {
        Menu {
            ForEach(options, id: \.0) { value, label in
                Button { selection = value } label: {
                    if selection == value { Label(label, systemImage: "checkmark") }
                    else { Text(label) }
                }
            }
        } label: {
            HStack(spacing: 8) {
                Text(selectedLabel).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
                Image(systemName: "chevron.up.chevron.down").font(.system(size: 9, weight: .semibold)).foregroundStyle(Palette.muted)
            }.font(.system(size: 13)).foregroundStyle(Palette.ink)
                .padding(.horizontal, 10).frame(height: height).controlSurface()
                .contentShape(Rectangle())
        }
        .menuStyle(.borderlessButton).menuIndicator(.hidden)
        .accessibilityLabel(title).accessibilityValue(selectedLabel).help(selectedLabel)
        .opacity(enabled && !options.isEmpty ? 1 : 0.52).disabled(options.isEmpty)
    }
}

struct SidebarRowStyle: ButtonStyle {
    var selected = false
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(maxWidth: .infinity, alignment: .leading)
            .frame(minHeight: 33)
            .padding(.horizontal, 9)
            .background(selected || configuration.isPressed ? Palette.wash : Color.clear, in: RoundedRectangle(cornerRadius: 8))
            .contentShape(Rectangle())
    }
}

struct Card<Content: View>: View {
    var padding: CGFloat = 20
    @ViewBuilder var content: Content
    var body: some View { content.padding(padding).frame(maxWidth: .infinity, alignment: .leading).background(Palette.paper, in: RoundedRectangle(cornerRadius: 14)).overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(Palette.border, lineWidth: 0.6)) }
}

struct DialogHeader: View {
    var title: String
    var nativeClose = false
    var close: () -> Void
    var body: some View {
        HStack(spacing: 12) {
            if nativeClose { NativeCloseButton(action: close).frame(width: 16, height: 16) }
            else { OwlImage().frame(width: 27, height: 27) }
            Text(title).font(.headline)
            Spacer()
            if !nativeClose {
                Button(action: close) { Image(systemName: "xmark").font(.system(size: 12, weight: .semibold)).frame(width: 28, height: 28) }
                    .buttonStyle(.borderless).foregroundStyle(.secondary).help("关闭").accessibilityLabel("关闭")
            }
        }.padding(.horizontal, 16).frame(height: 45).background(Palette.chrome).overlay(alignment: .bottom) { Palette.border.frame(height: 1) }
    }
}

private struct NativeCloseButton: NSViewRepresentable {
    var action: () -> Void
    func makeNSView(context: Context) -> NSButton {
        let button = NSWindow.standardWindowButton(.closeButton, for: [.titled, .closable]) ?? NSButton()
        button.target = context.coordinator
        button.action = #selector(Coordinator.close)
        button.isHidden = false
        button.toolTip = "关闭设置"
        button.setAccessibilityLabel("关闭设置")
        return button
    }
    func updateNSView(_ button: NSButton, context: Context) { context.coordinator.action = action }
    func makeCoordinator() -> Coordinator { Coordinator(action) }
    final class Coordinator: NSObject {
        var action: () -> Void
        init(_ action: @escaping () -> Void) { self.action = action }
        @objc func close() { action() }
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
    func controlSurface() -> some View {
        background(Palette.control, in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Palette.border, lineWidth: 0.65).allowsHitTesting(false))
    }
    func inputFrame(height: CGFloat = 34) -> some View { textFieldStyle(.plain).padding(.horizontal, 10).frame(height: height).controlSurface() }
    func sectionLabel() -> some View { self.font(.system(size: 11, weight: .semibold)).foregroundStyle(Palette.muted) }
}
