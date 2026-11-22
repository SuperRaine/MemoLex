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

struct LookupButtonStyle: ButtonStyle {
    var primary = false
    var small = false
    var expands = false
    var fixedWidth: CGFloat?
    @Environment(\.isEnabled) private var enabled

    func makeBody(configuration: Configuration) -> some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        let fill = primary ? Palette.accent : Palette.wash
        return configuration.label
            .font(.system(size: small ? 11 : 12, weight: primary ? .semibold : .regular))
            .lineLimit(1)
            .padding(.horizontal, primary ? 15 : 10)
            .frame(maxWidth: expands ? .infinity : nil)
            .frame(width: fixedWidth, height: small ? 27 : 34)
            .foregroundStyle(primary ? Palette.accentInk : Palette.deep)
            .background(fill.opacity(configuration.isPressed ? 0.76 : 1), in: shape)
            .overlay(shape.strokeBorder(primary ? Palette.accent : Palette.border, lineWidth: 0.75))
            .contentShape(shape)
            .opacity(enabled ? 1 : 0.43)
    }
}

struct MemoPicker<Value: Hashable>: View {
    var title: String
    @Binding var selection: Value
    var options: [(Value, String)]
    var placeholder: String? = nil
    var height: CGFloat = 34
    @Environment(\.isEnabled) private var enabled
    @State private var expanded = false
    @State private var hovered = false

    private var selectedLabel: String { options.first { $0.0 == selection }?.1 ?? placeholder ?? title }

    var body: some View {
        Button { expanded.toggle() } label: {
            HStack(spacing: 8) {
                Text(selectedLabel).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
                    .foregroundStyle(options.contains { $0.0 == selection } ? Palette.ink : Palette.muted)
                DropdownChevron(expanded: expanded)
            }.font(.system(size: 13, weight: .medium))
                .padding(.horizontal, 11).frame(height: height)
                .dropdownSurface(active: expanded, hovered: hovered && enabled && !options.isEmpty)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovered = $0 }
        .accessibilityLabel(title).accessibilityValue(selectedLabel).help(selectedLabel)
        .dropdownOptions(expanded: $expanded, selection: selection, options: options) { selection = $0 }
        .opacity(enabled && !options.isEmpty ? 1 : 0.55).disabled(options.isEmpty)
    }
}

struct DropdownChevron: View {
    var expanded: Bool

    var body: some View {
        Image(systemName: "chevron.down")
            .font(.system(size: 10, weight: .semibold))
            .rotationEffect(.degrees(expanded ? 180 : 0))
            .foregroundStyle(expanded ? Palette.deep : Palette.muted)
            .frame(width: 14, height: 14)
            .accessibilityHidden(true)
    }
}

private struct DropdownWidth: PreferenceKey {
    static let defaultValue: CGFloat = 0

    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = max(value, nextValue())
    }
}

private struct DropdownList<Value: Hashable>: View {
    var selection: Value?
    var options: [(Value, String)]
    var select: (Value) -> Void
    var dismiss: () -> Void
    @FocusState private var focused: Bool
    @State private var highlighted: Value?
    @State private var hovered: Value?

    var body: some View {
        ScrollViewReader { scroll in
            ScrollView {
                VStack(spacing: 2) {
                    ForEach(options, id: \.0) { value, label in option(value, label: label).id(value) }
                }.padding(5)
            }
            .frame(height: CGFloat(min(options.count, 8)) * 32 + 8)
            .focusable().focused($focused)
            .onAppear {
                highlighted = options.first { $0.0 == selection }?.0 ?? options.first?.0
                focused = true
            }
            .onChange(of: highlighted) { value in
                if let value { scroll.scrollTo(value, anchor: .center) }
            }
            .onMoveCommand(perform: moveHighlight)
            .onExitCommand(perform: dismiss)
        }
    }

    private func option(_ value: Value, label: String) -> some View {
        let selected = value == selection
        let emphasized = (hovered ?? highlighted) == value
        return Button { select(value) } label: {
            HStack(spacing: 9) {
                Text(label).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
                Image(systemName: "checkmark")
                    .font(.system(size: 10, weight: .semibold)).opacity(selected ? 1 : 0)
                    .accessibilityHidden(true)
            }
            .font(.system(size: 13, weight: selected ? .semibold : .regular))
            .foregroundStyle(selected ? Palette.deep : Palette.ink)
            .padding(.horizontal, 9).frame(height: 30)
            .background(selected || emphasized ? Palette.wash : Color.clear, in: RoundedRectangle(cornerRadius: 6))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selected ? .isSelected : [])
        .keyboardShortcut(self.highlighted == value ? KeyboardShortcut(.return, modifiers: []) : nil)
        .onHover { if $0 { hovered = value } else if hovered == value { hovered = nil } }
    }

    private func moveHighlight(_ direction: MoveCommandDirection) {
        guard !options.isEmpty, direction == .up || direction == .down else { return }
        let index = options.firstIndex { $0.0 == highlighted } ?? 0
        let next = index + (direction == .up ? -1 : 1)
        hovered = nil
        highlighted = options[min(options.count - 1, max(0, next))].0
    }
}

private struct DropdownOptions<Value: Hashable>: ViewModifier {
    @Binding var expanded: Bool
    var selection: Value?
    var options: [(Value, String)]
    var minimumWidth: CGFloat
    var select: (Value) -> Void
    @State private var width: CGFloat = 0

    func body(content: Content) -> some View {
        content
            .background { GeometryReader { geometry in Color.clear.preference(key: DropdownWidth.self, value: geometry.size.width) } }
            .onPreferenceChange(DropdownWidth.self) { width = $0 }
            .popover(isPresented: $expanded, arrowEdge: .bottom) {
                DropdownList(selection: selection, options: options, select: {
                    expanded = false
                    select($0)
                }, dismiss: { expanded = false })
                .frame(width: max(width, minimumWidth))
                .background(Palette.paper)
            }
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
    var close: () -> Void
    var body: some View {
        HStack(spacing: 12) {
            NativeCloseButton(title: "关闭\(title)", action: close).frame(width: 16, height: 16)
            Text(title).font(.headline)
            Spacer()
        }.padding(.horizontal, 16).frame(height: 45).background(Palette.chrome).overlay(alignment: .bottom) { Palette.border.frame(height: 1) }
    }
}

private struct NativeCloseButton: NSViewRepresentable {
    var title: String
    var action: () -> Void
    func makeNSView(context: Context) -> NSButton {
        let button = NSWindow.standardWindowButton(.closeButton, for: [.titled, .closable]) ?? NSButton()
        button.target = context.coordinator
        button.action = #selector(Coordinator.close)
        button.isHidden = false
        button.toolTip = title
        button.setAccessibilityLabel(title)
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
    func dropdownOptions<Value: Hashable>(expanded: Binding<Bool>, selection: Value?, options: [(Value, String)],
                                           minimumWidth: CGFloat = 126, select: @escaping (Value) -> Void) -> some View {
        modifier(DropdownOptions(expanded: expanded, selection: selection, options: options, minimumWidth: minimumWidth, select: select))
    }
    func dropdownSurface(active: Bool, hovered: Bool) -> some View {
        let shape = RoundedRectangle(cornerRadius: 8)
        return background(active ? Palette.wash : Palette.control, in: shape)
            .overlay(shape.strokeBorder(active || hovered ? Palette.accent : Palette.border, lineWidth: active ? 1.15 : 1)
                .allowsHitTesting(false))
    }
    func controlSurface() -> some View {
        background(Palette.control, in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Palette.border, lineWidth: 0.65).allowsHitTesting(false))
    }
    func inputFrame(height: CGFloat = 34) -> some View { textFieldStyle(.plain).padding(.horizontal, 10).frame(height: height).controlSurface() }
    func sectionLabel() -> some View { self.font(.system(size: 11, weight: .semibold)).foregroundStyle(Palette.muted) }
}
