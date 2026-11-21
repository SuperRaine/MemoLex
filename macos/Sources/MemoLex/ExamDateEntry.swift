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
                          commit: finishEditing)
                .padding(.horizontal, 6).frame(width: width, height: 34).controlSurface()
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

    private func finishEditing() {
        if let y = Int(year), (1...9999).contains(y) { year = Self.yearText(y) }
        saveDate(clampDay: false)
        let parts = Self.calendar.dateComponents([.year, .month, .day], from: date)
        year = Self.yearText(parts.year ?? 1)
        month = String(parts.month ?? 1)
        day = String(parts.day ?? 1)
    }
}

private struct DateNumberBox: NSViewRepresentable {
    var title: String
    @Binding var text: String
    var values: [Int]
    var range: ClosedRange<Int>
    var digits: Int
    var commit: () -> Void

    func makeNSView(context: Context) -> NSComboBox {
        let field = NSComboBox()
        field.isEditable = true
        field.isSelectable = true
        field.completes = false
        field.drawsBackground = false
        field.isBezeled = false
        field.isBordered = false
        field.isButtonBordered = false
        field.numberOfVisibleItems = 8
        field.font = .monospacedDigitSystemFont(ofSize: 13, weight: .regular)
        field.setAccessibilityLabel(title)
        field.toolTip = "输入\(title)或从列表选择，也可以按 ↑ ↓ 调整"
        field.delegate = context.coordinator
        return field
    }

    func updateNSView(_ field: NSComboBox, context: Context) {
        let coordinator = context.coordinator
        coordinator.parent = self
        if coordinator.options != values {
            coordinator.options = values
            field.removeAllItems()
            field.addItems(withObjectValues: values.map(String.init))
        }
        field.isEnabled = context.environment.isEnabled
        if field.stringValue != text { field.stringValue = text }
    }

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    final class Coordinator: NSObject, NSComboBoxDelegate {
        var parent: DateNumberBox
        var options: [Int] = []
        private var menuOpen = false

        init(_ parent: DateNumberBox) { self.parent = parent }

        func comboBoxWillPopUp(_ notification: Notification) { menuOpen = true }
        func comboBoxWillDismiss(_ notification: Notification) { menuOpen = false }

        func comboBoxSelectionDidChange(_ notification: Notification) {
            guard let field = notification.object as? NSComboBox,
                  options.indices.contains(field.indexOfSelectedItem) else { return }
            set(options[field.indexOfSelectedItem], on: field)
        }

        func controlTextDidChange(_ notification: Notification) {
            guard let field = notification.object as? NSComboBox else { return }
            let filtered = field.stringValue.prefix(parent.digits).compactMap(\.wholeNumberValue)
                .filter { (0...9).contains($0) }.map(String.init).joined()
            if filtered != field.stringValue { field.currentEditor()?.string = filtered }
            parent.text = filtered
        }

        func controlTextDidEndEditing(_ notification: Notification) { parent.commit() }

        func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
            guard !menuOpen, let field = control as? NSComboBox else { return false }
            let change: Int
            switch selector {
            case #selector(NSResponder.moveUp(_:)): change = 1
            case #selector(NSResponder.moveDown(_:)): change = -1
            default: return false
            }
            let value = (Int(parent.text) ?? parent.range.lowerBound) + change
            set(min(parent.range.upperBound, max(parent.range.lowerBound, value)), on: field)
            return true
        }

        private func set(_ number: Int, on field: NSComboBox) {
            let value = parent.digits == 4 ? String(format: "%04d", number) : String(number)
            field.stringValue = value
            parent.text = value
        }
    }
}
