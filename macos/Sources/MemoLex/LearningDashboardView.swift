import SwiftUI

struct LearningDashboardView: View {
    @ObservedObject var model: AppModel

    private var days: [Date] {
        let cal = Calendar.current
        let today = cal.startOfDay(for: Date())
        return (0..<7).compactMap { cal.date(byAdding: .day, value: -6 + $0, to: today) }
    }

    private var dailyCounts: [Int] {
        let cal = Calendar.current
        return days.map { day in
            model.history.filter { cal.isDate($0.date, inSameDayAs: day) }.count
        }
    }

    private var weekTotal: Int { dailyCounts.reduce(0, +) }
    private var totalLookups: Int { model.history.reduce(0) { $0 + $1.count } }
    private var repeatedCount: Int { model.history.filter { $0.count >= 3 }.count }
    private var masteredCount: Int {
        guard let progress = model.loadFlashcardProgress() else { return 0 }
        return max(0, progress.cursor - progress.queue.count)
    }

    private var topRepeated: [SearchRecord] {
        model.history
            .filter { $0.count >= 2 }
            .sorted { a, b in
                if a.count != b.count { return a.count > b.count }
                return a.date > b.date
            }
            .prefix(5)
            .map { $0 }
    }

    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "学习统计看板") { model.sheet = nil }
            ScrollView {
                VStack(alignment: .leading, spacing: 15) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("学习统计看板").font(.system(size: 22, weight: .bold))
                        Text("统计本机查词与自测记录，点击单词可直接复习。")
                            .font(.system(size: 12)).foregroundStyle(Palette.muted)
                    }

                    HStack(spacing: 10) {
                        kpiCard("近 7 天查词", value: "\(weekTotal)", hint: "查过的词", tint: Color(red: 0.05, green: 0.65, blue: 0.91))
                        kpiCard("累计查词", value: "\(totalLookups)", hint: "共 \(model.history.count) 个不同词", tint: Color(red: 0.08, green: 0.72, blue: 0.65))
                        kpiCard("反复查词", value: "\(repeatedCount)", hint: "查询 ≥ 3 次", tint: Color(red: 0.85, green: 0.47, blue: 0.02))
                        kpiCard("卡片已掌握", value: "\(masteredCount)", hint: model.stubbornTotal > 0 ? "顽固词共 \(model.stubbornTotal)" : "来自卡片自测", tint: Color(red: 0.06, green: 0.72, blue: 0.50))
                    }

                    VStack(alignment: .leading, spacing: 12) {
                        Text("近 7 天查词趋势").font(.system(size: 13.5, weight: .semibold))
                        let counts = dailyCounts
                        let maxVal = max(1, counts.max() ?? 1)
                        HStack(alignment: .bottom, spacing: 10) {
                            ForEach(days.indices, id: \.self) { idx in
                                let count = counts[idx]
                                let barHeight = count == 0 ? 4.0 : max(12.0, 74.0 * Double(count) / Double(maxVal))
                                VStack(spacing: 5) {
                                    Text(count > 0 ? "\(count)" : "")
                                        .font(.system(size: 10.5, weight: .semibold))
                                        .foregroundStyle(Palette.deep)
                                        .frame(height: 14)
                                    RoundedRectangle(cornerRadius: 5, style: .continuous)
                                        .fill(count > 0
                                            ? AnyShapeStyle(LinearGradient(colors: [Palette.accent, Palette.deep], startPoint: .top, endPoint: .bottom))
                                            : AnyShapeStyle(Palette.border))
                                        .frame(width: 26, height: barHeight)
                                    Text(dayLabel(days[idx], isToday: idx == days.count - 1))
                                        .font(.system(size: 10.5, weight: idx == days.count - 1 ? .semibold : .regular))
                                        .foregroundStyle(idx == days.count - 1 ? Palette.ink : Palette.muted)
                                }
                                .frame(maxWidth: .infinity)
                            }
                        }
                        .frame(height: 112, alignment: .bottom)
                    }
                    .padding(16)
                    .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))

                    VStack(alignment: .leading, spacing: 10) {
                        Text("最常复查的词").font(.system(size: 13.5, weight: .semibold))
                        if topRepeated.isEmpty {
                            Text("暂无重复查询 2 次以上的单词，继续查词积累吧！")
                                .font(.system(size: 12)).foregroundStyle(Palette.muted)
                        } else {
                            MorphemeFlowLayout(spacing: 8, lineSpacing: 7, expandLast: false) {
                                ForEach(topRepeated) { record in
                                    Button("\(record.word) · \(record.count) 次") {
                                        model.sheet = nil
                                        model.lookup(record.word)
                                    }
                                    .buttonStyle(PillButtonStyle(compact: false))
                                }
                            }
                        }
                    }
                    .padding(16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Palette.canvas, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))

                    HStack(alignment: .center, spacing: 12) {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("例句自测与拼写巩固")
                                .font(.system(size: 13.5, weight: .semibold))
                            let mistakeSummary: String = {
                                if model.activeSpellingMistakes.isEmpty {
                                    return "拼写出错时会自动记下易错位置，连续拼对 2 次后自动清除"
                                }
                                let sample = model.activeSpellingMistakes.prefix(3).map {
                                    "\($0["word"].text)（\($0["correctOption"].text)/\($0["wrongOption"].text)）"
                                }.joined(separator: "、")
                                return "待巩固拼写 \(model.activeSpellingMistakes.count) 词：\(sample)"
                            }()
                            Text("已收藏 \(model.contextSentences.count) 条例句 · \(mistakeSummary)")
                                .font(.system(size: 11.5))
                                .foregroundStyle(Palette.muted)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        Spacer(minLength: 8)
                        Button(model.contextSentences.isEmpty ? "暂无例句" : "例句自测（\(model.contextSentences.count)）") {
                            model.openContextReview()
                        }
                        .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                        .disabled(model.contextSentences.isEmpty)
                    }
                    .padding(16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Palette.wash.opacity(0.65), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.accent.opacity(0.28), lineWidth: 0.85))

                    if model.preferences.showCountdown {
                        let cal = Calendar.current
                        let daysLeft = max(0, cal.dateComponents([.day], from: cal.startOfDay(for: Date()), to: cal.startOfDay(for: model.preferences.examDate)).day ?? 0)
                        HStack {
                            Text("目标考试：\(model.preferences.examName.trimmed.isEmpty ? "考试" : model.preferences.examName.trimmed)")
                                .font(.system(size: 12.5, weight: .semibold))
                            Spacer()
                            Text("还剩 \(daysLeft) 天")
                                .font(.system(size: 13, weight: .bold))
                        }
                        .foregroundStyle(Color(red: 0.65, green: 0.34, blue: 0.03))
                        .padding(.horizontal, 14).padding(.vertical, 10)
                        .background(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.14), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.35), lineWidth: 0.8))
                    }
                }
                .padding(22)
            }
        }
        .frame(width: 650, height: 590)
        .background(Palette.paper)
        .foregroundStyle(Palette.ink)
        .font(.system(size: 13))
        .noFocusRing()
        .task {
            await model.refreshContextSentences()
            await model.refreshActiveSpellingMistakes()
        }
    }

    private func kpiCard(_ title: String, value: String, hint: String, tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).font(.system(size: 11, weight: .semibold)).foregroundStyle(Palette.muted)
            Text(value).font(.system(size: 22, weight: .bold)).foregroundStyle(tint)
            Text(hint).font(.system(size: 10.5)).foregroundStyle(Palette.muted).lineLimit(1)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.wash.opacity(0.65), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
    }

    private func dayLabel(_ date: Date, isToday: Bool) -> String {
        if isToday { return "今天" }
        let formatter = DateFormatter()
        formatter.dateFormat = "MM/dd"
        return formatter.string(from: date)
    }
}
