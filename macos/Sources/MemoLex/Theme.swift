import AppKit
import SwiftUI

struct MemoButtonStyle: ButtonStyle {
    var accent = false
    var compact = false
    @Environment(\.isEnabled) private var enabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    func makeBody(configuration: Configuration) -> some View {
        MemoButtonChrome(configuration: configuration, accent: accent, compact: compact, enabled: enabled, reduceMotion: reduceMotion)
    }
}

private struct MemoButtonChrome: View {
    let configuration: ButtonStyle.Configuration
    let accent: Bool
    let compact: Bool
    let enabled: Bool
    let reduceMotion: Bool
    @State private var hovered = false

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
        configuration.label
            .font(.system(size: compact ? 12 : 13, weight: accent ? .semibold : .medium))
            .padding(.horizontal, compact ? 11 : 15)
            .frame(minHeight: compact ? 28 : 34)
            .foregroundStyle(accent ? Color.white : (hovered && enabled ? Palette.deep : Palette.ink))
            .background {
                if accent {
                    LinearGradient(
                        colors: hovered ? [Color(red: 0.24, green: 0.76, blue: 1.0), Color(red: 0.07, green: 0.59, blue: 0.86)]
                            : [Color(red: 0.17, green: 0.72, blue: 0.97), Color(red: 0.05, green: 0.53, blue: 0.78)],
                        startPoint: .topLeading, endPoint: .bottomTrailing
                    )
                    .opacity(configuration.isPressed ? 0.85 : 1)
                    .clipShape(shape)
                } else {
                    shape.fill(configuration.isPressed ? Palette.wash : (hovered && enabled ? Palette.wash.opacity(0.75) : Palette.control))
                }
            }
            .overlay(shape.strokeBorder(accent ? Color.white.opacity(0.18) : (hovered && enabled ? Palette.accent.opacity(0.68) : Palette.border), lineWidth: 0.8))
            .shadow(color: accent && enabled ? Color(red: 0.05, green: 0.53, blue: 0.78).opacity(hovered ? 0.34 : 0.22) : .clear, radius: hovered ? 8 : 5, y: 2)
            .opacity(enabled ? 1 : 0.42)
            .scaleEffect(enabled && configuration.isPressed && !reduceMotion ? 0.97 : 1)
            .animation(reduceMotion ? nil : .spring(response: 0.22, dampingFraction: 0.78), value: configuration.isPressed)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.16), value: hovered)
            .onHover { hovered = $0 }
    }
}

struct PillButtonStyle: ButtonStyle {
    var compact = true
    @Environment(\.isEnabled) private var enabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    func makeBody(configuration: Configuration) -> some View {
        PillButtonChrome(configuration: configuration, compact: compact, enabled: enabled, reduceMotion: reduceMotion)
    }
}

private struct PillButtonChrome: View {
    let configuration: ButtonStyle.Configuration
    let compact: Bool
    let enabled: Bool
    let reduceMotion: Bool
    @State private var hovered = false

    var body: some View {
        configuration.label
            .font(.system(size: compact ? 12 : 13, weight: .semibold))
            .padding(.horizontal, compact ? 11 : 14)
            .frame(minHeight: compact ? 27 : 31)
            .foregroundStyle(Palette.deep)
            .background(
                Capsule().fill(configuration.isPressed ? Palette.accent.opacity(0.24) : (hovered && enabled ? Palette.accent.opacity(0.18) : Palette.wash))
            )
            .overlay(
                Capsule().strokeBorder(hovered && enabled ? Palette.accent.opacity(0.75) : Palette.accent.opacity(0.28), lineWidth: 0.85)
            )
            .contentShape(Capsule())
            .opacity(enabled ? 1 : 0.42)
            .scaleEffect(enabled && configuration.isPressed && !reduceMotion ? 0.97 : 1)
            .animation(reduceMotion ? nil : .spring(response: 0.2, dampingFraction: 0.8), value: configuration.isPressed)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.14), value: hovered)
            .onHover { hovered = $0 }
    }
}

struct LookupButtonStyle: ButtonStyle {
    var primary = false
    var small = false
    var expands = false
    var fixedWidth: CGFloat?
    @Environment(\.isEnabled) private var enabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func makeBody(configuration: Configuration) -> some View {
        LookupButtonChrome(configuration: configuration, primary: primary, small: small, expands: expands, fixedWidth: fixedWidth, enabled: enabled, reduceMotion: reduceMotion)
    }
}

private struct LookupButtonChrome: View {
    let configuration: ButtonStyle.Configuration
    let primary: Bool
    let small: Bool
    let expands: Bool
    let fixedWidth: CGFloat?
    let enabled: Bool
    let reduceMotion: Bool
    @State private var hovered = false

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
        configuration.label
            .font(.system(size: small ? 11 : 12, weight: primary ? .semibold : .medium))
            .lineLimit(1)
            .padding(.horizontal, primary ? 15 : small && expands ? 6 : 10)
            .frame(maxWidth: expands ? .infinity : nil)
            .frame(width: fixedWidth, height: small ? 28 : 34)
            .foregroundStyle(primary ? Color.white : Palette.deep)
            .background {
                if primary {
                    LinearGradient(
                        colors: hovered ? [Color(red: 0.24, green: 0.76, blue: 1.0), Color(red: 0.07, green: 0.59, blue: 0.86)]
                            : [Color(red: 0.17, green: 0.72, blue: 0.97), Color(red: 0.05, green: 0.53, blue: 0.78)],
                        startPoint: .topLeading, endPoint: .bottomTrailing
                    )
                    .opacity(configuration.isPressed ? 0.84 : 1)
                    .clipShape(shape)
                } else {
                    shape.fill(Palette.wash.opacity(configuration.isPressed ? 0.78 : (hovered && enabled ? 1 : 0.88)))
                }
            }
            .overlay(shape.strokeBorder(primary ? Color.white.opacity(0.18) : (hovered && enabled ? Palette.accent.opacity(0.75) : Palette.accent.opacity(0.28)), lineWidth: 0.85))
            .shadow(color: primary && enabled ? Color(red: 0.05, green: 0.53, blue: 0.78).opacity(hovered ? 0.34 : 0.22) : .clear, radius: hovered ? 8 : 5, y: 2)
            .contentShape(shape)
            .opacity(enabled ? 1 : 0.43)
            .scaleEffect(enabled && configuration.isPressed && !reduceMotion ? 0.97 : 1)
            .animation(reduceMotion ? nil : .spring(response: 0.22, dampingFraction: 0.78), value: configuration.isPressed)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.16), value: hovered)
            .onHover { hovered = $0 }
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
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Image(systemName: "chevron.down")
            .font(.system(size: 10, weight: .semibold))
            .rotationEffect(.degrees(expanded ? 180 : 0))
            .foregroundStyle(expanded ? Palette.deep : Palette.muted)
            .frame(width: 14, height: 14)
            .accessibilityHidden(true)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.17), value: expanded)
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
    static let height: CGFloat = 33
    var selected = false
    func makeBody(configuration: Configuration) -> some View {
        SidebarRowChrome(configuration: configuration, selected: selected)
    }
}

private struct SidebarRowChrome: View {
    let configuration: ButtonStyle.Configuration
    let selected: Bool
    @State private var hovered = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(spacing: 7) {
            Capsule()
                .fill(Palette.accent)
                .frame(width: 3, height: selected ? 16 : 0)
                .opacity(selected ? 1 : 0)
            configuration.label
                .foregroundStyle(selected ? Palette.deep : Palette.ink)
                .fontWeight(selected ? .semibold : .regular)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .frame(minHeight: SidebarRowStyle.height)
        .padding(.horizontal, 8)
        .background(
            selected || configuration.isPressed ? Palette.wash : (hovered ? Palette.wash.opacity(0.55) : Color.clear),
            in: RoundedRectangle(cornerRadius: 8, style: .continuous)
        )
        .contentShape(Rectangle())
        .animation(reduceMotion ? nil : .spring(response: 0.24, dampingFraction: 0.85), value: selected)
        .animation(reduceMotion ? nil : .easeOut(duration: 0.14), value: hovered)
        .onHover { hovered = $0 }
    }
}

struct Card<Content: View>: View {
    var padding: CGFloat = 20
    @ViewBuilder var content: Content
    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 15, style: .continuous)
        content
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Palette.paper, in: shape)
            .overlay(shape.strokeBorder(Palette.border, lineWidth: 0.7))
            .shadow(color: Color(red: 0.06, green: 0.24, blue: 0.36).opacity(0.06), radius: 14, y: 4)
    }
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
    var highlightPartOfSpeech: Bool = false
    private static let posRegex = try? NSRegularExpression(pattern: "(?m)^\\s*(?:n|v|vt|vi|adj|adv|prep|conj|pron|num|art|int|aux|abbr|pl|a|ad)\\.(?=\\s|$)")
    private var formatted: AttributedString {
        var value = AttributedString(text)
        value.foregroundColor = color
        let source = text as NSString
        var posRanges: [NSRange] = []
        if highlightPartOfSpeech, let posRegex = Self.posRegex {
            for match in posRegex.matches(in: text, range: NSRange(location: 0, length: source.length)) {
                posRanges.append(match.range)
                if let range = Range(match.range, in: text),
                   let start = AttributedString.Index(range.lowerBound, within: value),
                   let end = AttributedString.Index(range.upperBound, within: value) {
                    value[start..<end].foregroundColor = Palette.deep
                    value[start..<end].font = .system(size: max(13, size - 1), weight: .bold).italic()
                }
            }
        }
        let regex = try? NSRegularExpression(pattern: "[A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+(?:['’-][A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+)*")
        for match in regex?.matches(in: text, range: NSRange(location: 0, length: source.length)) ?? [] {
            if posRanges.contains(where: { NSIntersectionRange($0, match.range).length > 0 }) { continue }
            guard let range = Range(match.range, in: text),
                  let start = AttributedString.Index(range.lowerBound, within: value),
                  let end = AttributedString.Index(range.upperBound, within: value) else { continue }
            let token = String(text[range])
            if let encoded = token.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed), let url = URL(string: "memolex://word/\(encoded)") { value[start..<end].link = url }
            if highlights.contains(where: { $0.caseInsensitiveCompare(token) == .orderedSame }) {
                value[start..<end].backgroundColor = Palette.wash; value[start..<end].foregroundColor = Palette.deep; value[start..<end].font = .system(size: size, weight: .semibold)
            }
        }
        for term in chineseHighlights where !term.isEmpty {
            var cursor = text.startIndex
            while cursor < text.endIndex, let range = text.range(of: term, range: cursor..<text.endIndex) {
                if let start = AttributedString.Index(range.lowerBound, within: value),
                   let end = AttributedString.Index(range.upperBound, within: value) {
                    value[start..<end].backgroundColor = Palette.wash; value[start..<end].foregroundColor = Palette.deep
                }
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
