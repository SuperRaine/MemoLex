import AppKit
import SwiftUI

struct ExamDateEntry: View {
    @Binding var date: Date
    @State private var year: String
    @State private var month: String
    @State private var day: String
    private static let calendar = Calendar(identifier: .gregorian)

    init(date: Binding<Date>) {
        _date = date
        let parts = Self.calendar.dateComponents([.year, .month, .day], from: date.wrappedValue)
        _year = State(initialValue: Self.yearText(parts.year ?? 1))
        _month = State(initialValue: String(parts.month ?? 1))
        _day = State(initialValue: String(parts.day ?? 1))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(spacing: 9) {
                part("年", width: 96, text: $year, values: years, range: 1...9999, digits: 4, clampDay: true)
                part("月", width: 62, text: $month, values: Array(1...12), range: 1...12, digits: 2, clampDay: true)
                part("日", width: 62, text: $day, values: Array(1...daysInMonth), range: 1...daysInMonth, digits: 2, clampDay: false)
            }
            if let error { Text(error).font(.caption).foregroundStyle(.red) }
        }
    }

    private var years: [Int] {
        let current = Self.calendar.component(.year, from: Date())
        let range = (current - 2)...(current + 25)
        let saved = Self.calendar.component(.year, from: date)
        return range.contains(saved) ? Array(range) : (Array(range) + [saved]).sorted()
    }

    nonisolated static func days(year: Int, month: Int) -> Int {
        let calendar = Calendar(identifier: .gregorian)
        guard (1...9999).contains(year), (1...12).contains(month),
              let first = calendar.date(from: DateComponents(year: year, month: month, day: 1)) else { return 31 }
        return calendar.range(of: .day, in: .month, for: first)?.count ?? 31
    }

    private var daysInMonth: Int { Self.days(year: Int(year) ?? 0, month: Int(month) ?? 0) }

    private var error: String? {
        if year.count == 4, Int(year) == 0 { return "年份应在 1 到 9999 之间。" }
        if let value = Int(month), !(1...12).contains(value) { return "月份应在 1 到 12 之间。" }
        if let value = Int(day), !(1...daysInMonth).contains(value) { return "日期应在 1 到 \(daysInMonth) 之间。" }
        return nil
    }

    private func part(_ unit: String, width: CGFloat, text: Binding<String>, values: [Int],
                      range: ClosedRange<Int>, digits: Int, clampDay: Bool) -> some View {
        HStack(spacing: 5) {
            let binding = Binding(get: { text.wrappedValue }, set: {
                text.wrappedValue = $0
                saveDate(clampDay: clampDay)
            })
            DateNumberBox(title: unit, text: binding, values: values, range: range, digits: digits,
                          commit: { finishEditing(clampDay: clampDay) })
                .frame(width: width)
            Text(unit).font(.callout).foregroundStyle(Palette.muted).accessibilityHidden(true)
        }
    }

    private static func yearText(_ value: Int) -> String { String(format: "%04d", value) }

    private func saveDate(clampDay: Bool) {
        guard year.count == 4, let y = Int(year), (1...9999).contains(y), let m = Int(month),
              (1...12).contains(m), let d = Int(day), d > 0 else { return }
        let last = Self.days(year: y, month: m)
        let selectedDay = clampDay ? min(d, last) : d
        guard selectedDay <= last, let value = Self.calendar.date(from: DateComponents(year: y, month: m, day: selectedDay)) else { return }
        if d != selectedDay { day = String(selectedDay) }
        if !Self.calendar.isDate(value, inSameDayAs: date) { date = value }
    }

    private func finishEditing(clampDay: Bool) {
        if let y = Int(year), (1...9999).contains(y) { year = Self.yearText(y) }
        saveDate(clampDay: clampDay)
        let parts = Self.calendar.dateComponents([.year, .month, .day], from: date)
        year = Self.yearText(parts.year ?? 1)
        month = String(parts.month ?? 1)
        day = String(parts.day ?? 1)
    }
}

private struct DateNumberBox: View {
    var title: String
    @Binding var text: String
    var values: [Int]
    var range: ClosedRange<Int>
    var digits: Int
    var commit: () -> Void
    @Environment(\.isEnabled) private var enabled
    @State private var expanded = false
    @State private var hovered = false
    @State private var editing = false

    var body: some View {
        HStack(spacing: 0) {
            DateNumberField(title: title, text: $text, range: range, digits: digits,
                            commit: commit, editing: { editing = $0 })
                .frame(height: 20)
                .padding(.leading, 9)
            Button { expanded.toggle() } label: {
                DropdownChevron(expanded: expanded)
                    .frame(width: 26, height: 34)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("选择\(title)")
        }
        .frame(height: 34)
        .dropdownSurface(active: expanded || editing, hovered: hovered && enabled)
        .onHover { hovered = $0 }
        .dropdownOptions(expanded: $expanded, selection: Int(text),
                         options: values.map { ($0, String($0)) }, minimumWidth: 80) { value in
            text = digits == 4 ? String(format: "%04d", value) : String(value)
            commit()
        }
        .opacity(enabled ? 1 : 0.55)
    }
}

private struct DateNumberField: NSViewRepresentable {
    var title: String
    @Binding var text: String
    var range: ClosedRange<Int>
    var digits: Int
    var commit: () -> Void
    var editing: (Bool) -> Void

    func makeNSView(context: Context) -> NSTextField {
        let field = NSTextField()
        field.isEditable = true
        field.isSelectable = true
        field.drawsBackground = false
        field.isBezeled = false
        field.isBordered = false
        field.focusRingType = .none
        field.cell?.usesSingleLineMode = true
        field.font = .monospacedDigitSystemFont(ofSize: 13, weight: .medium)
        field.textColor = NSColor(Palette.ink)
        field.setContentHuggingPriority(.defaultLow, for: .horizontal)
        field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        field.setAccessibilityLabel(title)
        field.toolTip = "可直接输入，也可以按 ↑ ↓ 调整"
        field.delegate = context.coordinator
        return field
    }

    func updateNSView(_ field: NSTextField, context: Context) {
        context.coordinator.parent = self
        field.isEnabled = context.environment.isEnabled
        if let editor = field.currentEditor() {
            if editor.string != text { editor.string = text }
        } else if field.stringValue != text {
            field.stringValue = text
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    final class Coordinator: NSObject, NSTextFieldDelegate {
        var parent: DateNumberField

        init(_ parent: DateNumberField) { self.parent = parent }

        func controlTextDidBeginEditing(_ notification: Notification) { parent.editing(true) }

        func controlTextDidChange(_ notification: Notification) {
            guard let field = notification.object as? NSTextField, let editor = field.currentEditor() else { return }
            let entered = editor.string
            let filtered = entered.compactMap(\.wholeNumberValue).filter { (0...9).contains($0) }
                .prefix(parent.digits).map(String.init).joined()
            if filtered != entered { editor.string = filtered }
            parent.text = filtered
        }

        func controlTextDidEndEditing(_ notification: Notification) {
            parent.editing(false)
            parent.commit()
        }

        func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
            let change: Int
            switch selector {
            case #selector(NSResponder.moveUp(_:)): change = 1
            case #selector(NSResponder.moveDown(_:)): change = -1
            default: return false
            }
            let next = (Int(parent.text) ?? parent.range.lowerBound) + change
            let number = min(parent.range.upperBound, max(parent.range.lowerBound, next))
            let value = parent.digits == 4 ? String(format: "%04d", number) : String(number)
            textView.string = value
            parent.text = value
            return true
        }
    }
}
