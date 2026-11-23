import SwiftUI

@MainActor
struct TodayStudyView: View {
    @ObservedObject var model: AppModel
    @State private var plan = J.null
    @State private var books: [J] = []
    @State private var bookID = ""
    @State private var loadedBook = ""
    @State private var cloudIDs: Set<String> = []
    @State private var submitted: Set<String> = []
    @State private var candidates: [String: J] = [:]
    @State private var selected: Set<String> = []
    @State private var manual = ""
    @State private var randomCount = ""
    @State private var filter = 0
    @State private var status = ""
    @State private var busy = false
    @State private var writing = false
    @State private var confirming = false

    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "今日学习") { if !writing { model.sheet = nil } }
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .top) {
                    Text(summary).font(.system(size: 14)).foregroundStyle(Palette.ink)
                        .fixedSize(horizontal: false, vertical: true)
                    Spacer()
                    Button("刷新") {
                        Task { await run("正在刷新今日学习…") {
                            try await refresh()
                            if books.isEmpty { try await readBooks() }
                            status = "今日学习已刷新"
                        } }
                    }.buttonStyle(MemoButtonStyle()).disabled(busy)
                }
                TabView {
                    duePage.tabItem { Text("待学词汇") }
                    selectionPage.tabItem { Text("加入新学") }
                }
                HStack(spacing: 8) {
                    if busy { ProgressView().controlSize(.small) }
                    Text(status.statusMessage).font(.system(size: 12)).foregroundStyle(Palette.muted)
                }.frame(minHeight: 20, alignment: .leading)
            }.padding(18)
        }
        .frame(width: 730, height: 595)
        .interactiveDismissDisabled(writing)
        .onChange(of: model.activeAccountID) { _ in model.sheet = nil }
        .onChange(of: model.loggedIn) { if !$0 { model.sheet = nil } }
        .task {
            await run("正在读取今日学习…") {
                do { try await refresh() }
                catch {
                    try? await readBooks()
                    throw error
                }
                try await readBooks()
                status = plan["newWordsNeeded"] == .null ? "请打开墨墨背单词并同步今日学习数据" : ""
            }
        }
        .alert("添加到今日新学", isPresented: $confirming) {
            Button("取消", role: .cancel) {}
            Button("添加") { Task { await submit() } }
        } message: {
            Text("将选中的 \(selected.count) 个单词添加到今日新学？\n\n学习状态依赖手机端同步。可在「墨墨背单词 → 我的 → 学习数据同步」开启自动同步。")
        }
    }

    private var unfinished: [J] { plan["items"].list.filter { !$0["isFinished"].flag } }
    private var summary: String {
        guard plan != .null else { return "今日学习进度待获取" }
        let new = unfinished.filter { $0["isNew"].flag }.count
        let needed = plan["newWordsNeeded"]
        let gap = needed == .null ? "新词缺口待同步" : "还需加入 \(needed.integer) 词"
        let limit = plan["items"].list.count == 1000 ? " · 词表最多显示前 1000 条" : ""
        return "\(plan["day"].text) · 已完成 \(plan["finished"].integer)/\(plan["target"].integer) · 待新学 \(new) · 待复习 \(unfinished.count - new)\n\(gap)\(limit)"
    }

    private var duePage: some View {
        let visible = unfinished.filter { filter == 0 || $0["isNew"].flag == (filter == 1) }
        return VStack(alignment: .leading, spacing: 9) {
            Picker("学习类型", selection: $filter) {
                Text("全部待学").tag(0); Text("待新学").tag(1); Text("待复习").tag(2)
            }.pickerStyle(.segmented).labelsHidden().frame(maxWidth: 295)
            if visible.isEmpty {
                Text(plan == .null ? "等待今日词表" : "没有符合条件的待学词汇")
                    .font(.system(size: 12)).foregroundStyle(Palette.muted).frame(maxHeight: .infinity)
            } else {
                List(Array(visible.enumerated()), id: \.offset) { _, item in
                    HStack {
                        Text(item["spelling"].text)
                        Spacer()
                        Text(item["kind"].text).font(.system(size: 12)).foregroundStyle(Palette.muted)
                    }
                }.listStyle(.plain)
            }
        }.padding(12)
    }

    private var selectionPage: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack(spacing: 8) {
                MemoPicker(title: "选择云词本", selection: $bookID, options: books.map { ($0["id"].text, $0["title"].text) }).frame(width: 158)
                Button("读取词本") { Task { await run("正在核对词本中的新词…", action: readCloud) } }.disabled(bookID.isEmpty)
                TextField("数量", text: $randomCount).inputFrame().frame(width: 60).help("随机选词数量")
                Button("随机选词") { Task { await run("正在随机选词…", action: pickRandom) } }.disabled(bookID.isEmpty)
            }.buttonStyle(MemoButtonStyle(compact: true)).disabled(busy)
            HStack(alignment: .top, spacing: 8) {
                TextField("输入英文单词或短语，换行或用逗号分隔", text: $manual, axis: .vertical)
                    .lineLimit(2...3).inputFrame(height: 54)
                Button("选入待确认") { Task { await run("正在核对输入的单词…", action: stageManual) } }
                    .buttonStyle(MemoButtonStyle(compact: true)).disabled(manual.trimmed.isEmpty)
            }.disabled(busy)
            List(candidates.keys.sorted { candidates[$0, default: .null]["spelling"].text.localizedStandardCompare(candidates[$1, default: .null]["spelling"].text) == .orderedAscending }, id: \.self) { id in
                Toggle(candidates[id, default: .null]["spelling"].text, isOn: Binding(
                    get: { selected.contains(id) }, set: { checked in
                        if checked { selected.insert(id) } else { selected.remove(id) }
                    })).toggleStyle(.checkbox)
            }.listStyle(.plain).disabled(busy)
            HStack {
                Text("已选 \(selected.count) 词").foregroundStyle(Palette.muted)
                Spacer()
                Button("确认添加") { confirming = true }.buttonStyle(MemoButtonStyle(accent: true))
                    .disabled(busy || selected.isEmpty || selected.count > 1000)
            }
        }.padding(12)
    }

    private func run(_ message: String, write: Bool = false, action: () async throws -> Void) async {
        guard !busy && model.loggedIn else { return }
        busy = true; writing = write; status = message
        defer { busy = false; writing = false }
        do { try await action() }
        catch is CancellationError { }
        catch { status = error.localizedDescription }
    }

    private func refresh() async throws {
        plan = try await model.core.request("study.todayPlan")
        let needed = plan["newWordsNeeded"]
        if needed != .null { randomCount = String(min(needed.integer, 1000)) }
    }

    private func readBooks() async throws {
        books = model.notepads
        if books.isEmpty { books = try await model.core.request("notepads.list").list }
        let preferred = model.preferences.selectedNotepad
        bookID = books.contains { $0["id"].text == preferred } ? preferred : books.first?["id"].text ?? ""
    }

    private func readCloud() async throws {
        let source = bookID
        let words = try await model.core.request("study.candidates", ["notepadId": .string(source)])["words"].list
        for id in cloudIDs.subtracting(selected) { candidates[id] = nil }
        cloudIDs = []
        for word in words {
            let id = word["id"].text
            if !submitted.contains(id) { candidates[id] = word; cloudIDs.insert(id) }
        }
        loadedBook = source
        status = "词本中有 \(cloudIDs.count) 个可选新词"
    }

    private func pickRandom() async throws {
        guard let amount = Int(randomCount.trimmed), (1...1000).contains(amount) else { status = "随机数量请输入 1 到 1000"; return }
        if loadedBook != bookID { try await readCloud() }
        selected.subtract(cloudIDs)
        let picks = cloudIDs.shuffled().prefix(min(amount, max(0, 1000 - selected.count)))
        selected.formUnion(picks)
        status = "已随机选中 \(picks.count) 词，确认后才会添加"
    }

    private func stageManual() async throws {
        let result = try await model.core.request("study.candidates", ["input": .string(manual)])
        for word in result["words"].list {
            let id = word["id"].text
            if !submitted.contains(id) {
                candidates[id] = word
                if selected.count < 1000 { selected.insert(id) }
            }
        }
        manual = ""
        status = "找到 \(result["words"].list.count) 个可选新词 · \(result["excluded"].integer) 个已在计划或未被墨墨收录"
    }

    private func submit() async {
        let ids = selected.sorted()
        guard (1...1000).contains(ids.count) else { return }
        let date = DateFormatter()
        date.locale = Locale(identifier: "en_US_POSIX")
        date.calendar = Calendar(identifier: .gregorian)
        date.timeZone = TimeZone(secondsFromGMT: 8 * 60 * 60)
        date.dateFormat = "yyyy-MM-dd"
        let day = plan["day"].text.isEmpty ? date.string(from: Date()) : plan["day"].text
        await run("正在确认并添加新词…", write: true) {
            let result = try await model.core.request("study.addBatch", ["ids": .strings(ids), "day": .string(day)])
            submitted.formUnion(ids); cloudIDs.subtract(ids)
            for id in ids { candidates[id] = nil }
            selected = []
            let message = result["summary"].text
            status = message
            do { try await refresh() }
            catch { status = message + " · 今日学习暂未刷新" }
            model.refreshTodayStatus()
        }
    }
}
