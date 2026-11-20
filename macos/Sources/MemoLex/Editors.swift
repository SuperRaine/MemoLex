import AppKit
import SwiftUI

struct HistoryView: View {
    @ObservedObject var model: AppModel
    @State private var filter = ""
    @State private var selected: Set<String> = []
    private var records: [SearchRecord] { model.history.filter { filter.trimmed.isEmpty || $0.word.localizedCaseInsensitiveContains(filter) } }
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "查词历史") { model.sheet = nil }
            VStack(alignment: .leading, spacing: 14) {
                HStack { Text("查词历史").font(.system(size: 23, weight: .semibold)); Spacer(); Text("\(model.history.count) 条记录").foregroundStyle(Palette.muted) }
                TextField("筛选单词", text: $filter).inputFrame()
                HStack { Button("全选当前结果") { selected = Set(records.map(\.id)) }; Button("删除选中") { model.removeHistory(selected); selected = [] }.disabled(selected.isEmpty); Spacer(); Button("清空记录") { if confirm("清空查词历史？", "此操作仅删除本机的查词记录。", action: "清空") { model.clearHistory(); selected = [] } } }.buttonStyle(MemoButtonStyle(compact: true))
                List(selection: $selected) {
                    ForEach(records) { record in HStack { Button(record.word) { model.sheet = nil; model.lookup(record.word) }.buttonStyle(.plain).foregroundStyle(Palette.deep); Spacer(); Text(record.date, format: .dateTime.year().month().day().hour().minute()).font(.system(size: 11)).foregroundStyle(Palette.muted); Text("\(record.count) 次").font(.system(size: 11)).foregroundStyle(Palette.muted).frame(width: 55, alignment: .trailing) }.padding(.vertical, 6).tag(record.id) }
                }.listStyle(.inset).overlay(RoundedRectangle(cornerRadius: 9).stroke(Palette.border))
            }.padding(23)
        }.frame(width: 700, height: 570).background(Palette.paper).font(.system(size: 13)).foregroundStyle(Palette.ink)
    }
}

struct StubbornView: View {
    @ObservedObject var model: AppModel
    @ObservedObject private var preferences: Preferences
    init(model: AppModel) { self.model = model; self.preferences = model.preferences }
    @State private var filter = ""
    @State private var meanings: [String: String] = [:]
    @State private var revealed: Set<String> = []
    private var visible: [J] { model.stubbornOrder.filter { value in filter.trimmed.isEmpty || value["spelling"].text.localizedCaseInsensitiveContains(filter) || meanings[value["spelling"].text, default: ""].localizedCaseInsensitiveContains(filter) } }
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "顽固单词") { model.sheet = nil }
            VStack(alignment: .leading, spacing: 13) {
                HStack(alignment: .center) { VStack(alignment: .leading, spacing: 5) { Text("顽固单词").font(.system(size: 24, weight: .semibold)); Text("\(model.activeAccount?.name ?? "当前账号") · \(visible.count) 个单词").font(.system(size: 12)).foregroundStyle(Palette.muted) }; Spacer(); Button("刷新") { Task { await model.refreshStubborn(force: true) } }.buttonStyle(MemoButtonStyle()).disabled(model.stubbornLoading) }
                TextField("筛选单词或释义", text: $filter).inputFrame()
                HStack(spacing: 10) {
                    Text("排序").foregroundStyle(Palette.muted)
                    Picker("", selection: $preferences.stubbornSort) { Text("复习时间").tag("review"); Text("字母顺序").tag("alphabetical"); Text("随机轮换").tag("random") }.labelsHidden().frame(width: 136).onChange(of: preferences.stubbornSort) { _ in model.reorderStubborn() }
                    if model.preferences.stubbornSort == "alphabetical" { Button(model.preferences.alphabetDescending ? "Z–A" : "A–Z") { model.preferences.alphabetDescending.toggle(); model.reorderStubborn() }.buttonStyle(MemoButtonStyle(compact: true)) }
                    Spacer()
                    Toggle("遮住释义", isOn: $preferences.concealMeanings).toggleStyle(.switch).controlSize(.small).onChange(of: preferences.concealMeanings) { _ in revealed = []; preferences.save() }
                }
                HStack { Text("单词").frame(width: 180, alignment: .leading); Text("释义"); Spacer() }.font(.system(size: 11)).foregroundStyle(Palette.muted).padding(10).background(Palette.wash)
                ScrollView {
                    LazyVStack(spacing: 2) { ForEach(visible, id: \.spellingText) { value in
                        let word = value["spelling"].text
                        HStack(alignment: .center, spacing: 10) {
                            Button(word) { model.sheet = nil; model.lookup(word) }.buttonStyle(.plain).foregroundStyle(Palette.deep).frame(width: 180, alignment: .leading)
                            Button { if model.preferences.concealMeanings { if revealed.contains(word) { revealed.remove(word) } else { revealed.insert(word) } } } label: {
                                let hidden = model.preferences.concealMeanings && !revealed.contains(word)
                                Text(hidden ? "点击查看释义" : meanings[word, default: "正在读取…"]).font(.system(size: 13)).foregroundStyle(hidden ? Palette.muted : Palette.ink).frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 8).padding(.vertical, 7).background(hidden ? Palette.wash : Color.clear, in: RoundedRectangle(cornerRadius: 5))
                            }.buttonStyle(.plain)
                        }.padding(.horizontal, 10).frame(minHeight: 35)
                    } }
                }.overlay(RoundedRectangle(cornerRadius: 8).stroke(Palette.border))
                if model.stubbornLoading { ProgressView().controlSize(.small) }
                Text(model.stubbornHint.isEmpty ? "点击单词查词；手机端同步学习数据后刷新。" : model.stubbornHint).font(.system(size: 11)).foregroundStyle(Palette.muted)
            }.padding(23)
        }.frame(width: 730, height: min(650, (NSScreen.main?.visibleFrame.height ?? 760) - 60)).background(Palette.paper).foregroundStyle(Palette.ink).font(.system(size: 13)).tint(Palette.deep)
            .task { await model.refreshStubborn() }
            .task(id: model.stubbornWords.map { $0["spelling"].text }.joined(separator: "\n")) {
                if let rows = try? await model.core.request("dictionary.batch", ["content": .string(model.stubbornWords.map { $0["spelling"].text }.joined(separator: "\n"))]) { meanings = Dictionary(rows.list.map { ($0["word"].text, $0["meaning"].text) }, uniquingKeysWith: { first, _ in first }) }
            }
    }
}
extension J { var spellingText: String { self["spelling"].text } }

struct MnemonicView: View {
    @ObservedObject var model: AppModel
    @State private var selected = ""
    @State private var type = "联想"
    @State private var text = ""
    @State private var id = UUID().uuidString
    @State private var cloudID: String?
    @State private var saving = false
    @State private var error = ""
    private let types = ["词根词缀", "固定搭配", "辨析", "语法", "联想", "合成", "近反义词", "派生", "词源", "谐音", "串记", "口诀", "扩展", "其他"]
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "编辑助记") { model.sheet = nil }
            VStack(alignment: .leading, spacing: 14) {
                Text("\(model.word) · 编辑助记").font(.system(size: 23, weight: .semibold))
                Picker("助记", selection: $selected) { Text("新建助记").tag(""); ForEach(model.notes, id: \.idText) { note in Text(note["noteType"].text + " · " + String(note["text"].text.prefix(35))).tag(note["id"].text) } }.onChange(of: selected) { value in load(value) }
                Picker("分类", selection: $type) { ForEach(types, id: \.self) { Text($0).tag($0) } }.frame(width: 230)
                TextEditor(text: $text).font(.system(size: 15)).padding(8).frame(maxWidth: .infinity, maxHeight: .infinity).overlay(RoundedRectangle(cornerRadius: 10).stroke(Palette.border))
                Text(error.isEmpty ? "本地保存后离线也能查看；登录后可同步到墨墨。" : error).font(.system(size: 12)).foregroundStyle(error.isEmpty ? Palette.muted : .red)
                HStack { Spacer(); Button("保存到本地") { Task { await save(cloud: false) } }; Button("同步到墨墨") { Task { await save(cloud: true) } }.disabled(!model.loggedIn) }.buttonStyle(MemoButtonStyle()).disabled(saving || text.trimmed.isEmpty || text.count > 20000)
            }.padding(23)
        }.frame(width: 700, height: 560).background(Palette.paper).foregroundStyle(Palette.ink).font(.system(size: 13))
            .onAppear { if let first = model.notes.first { selected = first["id"].text; load(selected) } }
    }
    private func load(_ value: String) {
        guard let note = model.notes.first(where: { $0["id"].text == value }) else { id = UUID().uuidString; type = "联想"; text = ""; cloudID = nil; return }
        id = note["id"].text; type = note["noteType"].text; text = note["text"].text; cloudID = note["cloudId"].text.isEmpty ? nil : note["cloudId"].text
    }
    private func save(cloud: Bool) async { saving = true; do { try await model.saveMnemonic(id: id, type: type, text: text, cloud: cloud, cloudID: cloudID); model.sheet = nil } catch { self.error = error.localizedDescription }; saving = false }
}

struct NotebookView: View {
    @ObservedObject var model: AppModel
    @State private var tab = 0
    @State private var original = J.null
    @State private var title = ""
    @State private var brief = ""
    @State private var content = ""
    @State private var rows: [J] = []
    @State private var selected: Set<String> = []
    @State private var filter = ""
    @State private var editing = false
    @State private var busy = false
    @State private var error = ""
    private var visible: [J] { rows.filter { filter.isEmpty || $0["word"].text.localizedCaseInsensitiveContains(filter) || $0["meaning"].text.localizedCaseInsensitiveContains(filter) } }
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "云词本") { model.sheet = nil }
            VStack(alignment: .leading, spacing: 13) {
                HStack { Text(original["title"].text).font(.system(size: 23, weight: .semibold)); Spacer(); Button("重新载入") { Task { await reload() } }.buttonStyle(MemoButtonStyle(compact: true)) }
                Picker("", selection: $tab) { Text("单词与释义").tag(0); Text("编辑词本").tag(1) }.pickerStyle(.segmented).labelsHidden()
                if tab == 0 { wordsPage } else { infoPage }
                Text(error).font(.system(size: 12)).foregroundStyle(.red)
            }.padding(23).disabled(busy)
        }.frame(width: 820, height: min(700, (NSScreen.main?.visibleFrame.height ?? 800) - 60)).background(Palette.paper).foregroundStyle(Palette.ink).font(.system(size: 13)).toggleStyle(.checkbox)
            .task { original = model.selectedPad; applyOriginal(); await describe() }
    }
    private var wordsPage: some View {
        VStack(alignment: .leading, spacing: 11) {
            TextField("筛选单词或释义", text: $filter).inputFrame()
            HStack { Button("全选当前结果") { selected = Set(visible.filter { !$0["isChapter"].flag }.map { $0["word"].text }) }; Button("清除选择") { selected = [] }; Button("删除选中") { Task { await removeSelected() } }.disabled(selected.isEmpty); Spacer(); Text("已选 \(selected.count) 个").font(.system(size: 11)).foregroundStyle(Palette.muted) }.buttonStyle(MemoButtonStyle(compact: true))
            List(selection: $selected) {
                ForEach(Array(visible.enumerated()), id: \.offset) { _, row in
                    if row["isChapter"].flag { Text(row["word"].text).font(.system(size: 12, weight: .semibold)).foregroundStyle(Palette.muted).padding(.vertical, 5) }
                    else { HStack(spacing: 14) { Button(row["word"].text) { model.sheet = nil; model.lookup(row["word"].text) }.buttonStyle(.plain).foregroundStyle(Palette.deep).frame(width: 170, alignment: .leading); Text(row["meaning"].text).lineLimit(2).font(.system(size: 12)).frame(maxWidth: .infinity, alignment: .leading) }.padding(.vertical, 5).tag(row["word"].text) }
                }
            }.listStyle(.inset).frame(maxHeight: .infinity).overlay(RoundedRectangle(cornerRadius: 8).stroke(Palette.border))
            DisclosureGroup("添加 / 编辑词条与分组", isExpanded: $editing) {
                VStack(alignment: .leading, spacing: 9) { TextEditor(text: $content).font(.system(size: 13, design: .monospaced)).padding(6).frame(height: 150).overlay(RoundedRectangle(cornerRadius: 8).stroke(Palette.border)); HStack { Text("每行一个词，# 开头为分组。").font(.system(size: 11)).foregroundStyle(Palette.muted); Spacer(); Button("保存词表") { Task { await save(infoOnly: false) } }.buttonStyle(MemoButtonStyle()) } }.padding(.top, 9)
            }
            Text("按住 ⌘ 多选，按住 ⇧ 连选。").font(.system(size: 11)).foregroundStyle(Palette.muted)
        }
    }
    private var infoPage: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("词本名称").foregroundStyle(Palette.muted); TextField("名称", text: $title).inputFrame()
            Text("简介").foregroundStyle(Palette.muted); TextEditor(text: $brief).font(.system(size: 14)).padding(8).frame(height: 150).overlay(RoundedRectangle(cornerRadius: 8).stroke(Palette.border))
            HStack { Spacer(); Button("保存词本信息") { Task { await save(infoOnly: true) } }.buttonStyle(MemoButtonStyle(accent: true)) }; Spacer()
        }
    }
    private func applyOriginal() { title = original["title"].text; brief = original["brief"].text; content = original["content"].text; selected = [] }
    private func describe() async { do { rows = try await model.core.request("dictionary.batch", ["content": .string(content)]).list } catch { self.error = error.localizedDescription } }
    private func reload() async { await model.selectNotepad(model.preferences.selectedNotepad); original = model.selectedPad; applyOriginal(); await describe() }
    private func save(infoOnly: Bool) async {
        let text = infoOnly ? original["content"].text : content
        if text.components(separatedBy: .newlines).allSatisfy({ $0.trimmed.isEmpty || $0.trimmed.hasPrefix("#") }) { await removeWhole(); return }
        busy = true
        do { try await model.saveNotebook(original: original, title: infoOnly ? title : original["title"].text, brief: infoOnly ? brief : original["brief"].text, content: text); original = model.selectedPad; applyOriginal(); await describe(); editing = false; error = "" }
        catch { self.error = error.localizedDescription }; busy = false
    }
    private func removeSelected() async {
        guard confirm("删除选中的 \(selected.count) 个单词？", "这些单词将从当前云词本移除。", action: "删除") else { return }
        do { let result = try await model.core.request("notepads.edit", ["content": .string(content), "words": .string(selected.joined(separator: "\n")), "action": .string("remove")]); content = result["content"].text; await save(infoOnly: false) }
        catch { self.error = error.localizedDescription }
    }
    private func removeWhole() async { if confirm("将删除整个云词本", "云词本至少需要保留一个单词。当前选择会清空词本，是否删除整个词本？", action: "删除词本") { await model.deleteNotebook() } }
}

@MainActor func confirm(_ title: String, _ message: String, action: String) -> Bool {
    let alert = NSAlert(); alert.messageText = title; alert.informativeText = message; alert.alertStyle = .warning; alert.addButton(withTitle: action); alert.addButton(withTitle: "取消")
    return alert.runModal() == .alertFirstButtonReturn
}
