import AppKit
import SwiftUI

struct MainView: View {
    @ObservedObject var model: AppModel
    @ObservedObject private var preferences: Preferences
    @FocusState private var searchFocused: Bool

    init(model: AppModel) {
        self.model = model
        self.preferences = model.preferences
    }

    var body: some View {
        GeometryReader { geometry in
            let compact = geometry.size.width < 950
            VStack(spacing: 0) {
                MainTitlebar(model: model, preferences: preferences, compact: compact)
                Divider().overlay(Palette.border)
                HStack(spacing: 0) {
                    sidebar(compact: compact).frame(width: compact ? 220 : 264)
                    Divider().overlay(Palette.border)
                    Group {
                        switch model.page {
                        case .word: WordPage(model: model, compact: compact)
                        case .chinese: ChinesePage(model: model)
                        case .translation: TranslationPage(model: model)
                        }
                    }.padding(compact ? 16 : 25).frame(maxWidth: .infinity, maxHeight: .infinity).tourTarget("reading")
                }.frame(maxHeight: .infinity)
                Divider().overlay(Palette.border)
                HStack {
                    Text(model.status).lineLimit(1).truncationMode(.tail)
                    Spacer()
                    Text(dictionarySummary)
                }.font(.caption).foregroundStyle(.secondary).padding(.horizontal, 18).frame(height: 27).background(.bar)
            }.background(Palette.canvas).foregroundStyle(Palette.ink)
                .overlayPreferenceValue(TourTargets.self) { anchors in TourOverlay(model: model, anchors: anchors, context: "main") }
        }
        .ignoresSafeArea(.container, edges: .top)
        .toggleStyle(.checkbox)
        .environment(\.openURL, OpenURLAction { url in
            if url.scheme == "memolex", let query = url.path.removingPercentEncoding { model.lookup(String(query.dropFirst())); return .handled }
            return .systemAction
        })
        .sheet(item: $model.sheet) { sheet in
            switch sheet {
            case .settings: SettingsView(model: model)
            case .history: HistoryView(model: model)
            case .stubborn: StubbornView(model: model)
            case .mnemonic: MnemonicView(model: model)
            case .notepad: NotebookView(model: model)
            }
        }
        .onAppear { model.focusSearch = { searchFocused = true } }
    }
    private var dictionarySummary: String {
        guard model.ready else { return "正在准备词典…" }
        return "内置词库 · \(model.wordCount.formatted()) 词目"
    }
    private func sidebar(compact: Bool) -> some View {
        VStack(spacing: 13) {
            VStack(spacing: 10) {
                HStack { Text("快速查词").sectionLabel(); Spacer(); Text(model.preferences.shortcuts["search"]?.label ?? "").font(.system(size: 10)).foregroundStyle(Palette.muted) }
                HStack(spacing: 8) {
                    Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                    TextField("英文或中文", text: $model.search).textFieldStyle(.plain)
                        .focused($searchFocused).onSubmit { model.lookup(model.search) }
                        .onChange(of: model.search) { _ in model.suggest() }
                    Button { model.lookup(model.search) } label: { Image(systemName: "return") }
                        .buttonStyle(.borderless).help("查词").accessibilityLabel("查词")
                }.padding(.horizontal, 9).frame(height: 34)
                    .background(Palette.paper, in: RoundedRectangle(cornerRadius: 8))
                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Palette.border, lineWidth: 0.6))
                    .tourTarget("search")
                HStack(spacing: 7) {
                    Button("段落翻译") { model.enterTranslation() }.buttonStyle(MemoButtonStyle(compact: true)).frame(maxWidth: .infinity)
                    Button("截图翻译") { model.captureScreen?() }.buttonStyle(MemoButtonStyle(compact: true)).frame(maxWidth: .infinity).tourTarget("screenshot")
                }
            }
            VStack(alignment: .leading, spacing: 7) {
                Text("单词建议").sectionLabel()
                ScrollView { LazyVStack(alignment: .leading, spacing: 1) {
                    ForEach(model.suggestions, id: \.self) { word in Button { model.lookup(word) } label: { Text(word).font(.system(size: 14)).frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 9).padding(.horizontal, 9) }.buttonStyle(.plain) }
                } }.frame(maxWidth: .infinity)
            }.frame(maxHeight: .infinity)
            VStack(alignment: .leading, spacing: 7) {
                HStack { Text("最近查词").sectionLabel(); Spacer(); Button("全部 \(model.history.count) 条") { model.sheet = .history }.buttonStyle(.plain).font(.system(size: 10)).foregroundStyle(Palette.deep) }
                ScrollView { LazyVStack(alignment: .leading, spacing: 1) {
                    if model.history.isEmpty { Text("查过的单词会出现在这里").font(.system(size: 11)).foregroundStyle(Palette.muted).padding(9) }
                    ForEach(model.history.prefix(60)) { record in Button { model.lookup(record.word) } label: { HStack { Text(record.word); Spacer(); Image(systemName: "arrow.right").font(.system(size: 9)).foregroundStyle(Palette.muted) }.font(.system(size: 14)).padding(.vertical, 9).padding(.horizontal, 9).contentShape(Rectangle()) }.buttonStyle(.plain) }
                } }
            }.frame(maxHeight: .infinity)
            cloudCard.tourTarget("cloud")
        }.padding(.horizontal, compact ? 14 : 17).padding(.vertical, 15).background(.regularMaterial)
    }
    private var cloudCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack { Text("我的云词本").font(.system(size: 12, weight: .semibold)); Spacer(); Button { Task { await model.refreshNotepads() } } label: { Image(systemName: "arrow.clockwise") }.buttonStyle(.plain) }
            Picker("", selection: Binding(get: { model.preferences.selectedNotepad }, set: { id in Task { await model.selectNotepad(id) } })) {
                if model.notepads.isEmpty { Text("选择词本").tag("") }
                ForEach(model.notepads, id: \.idText) { pad in Text(pad["title"].text).tag(pad["id"].text) }
            }.labelsHidden().disabled(!model.loggedIn).frame(maxWidth: .infinity)
            Text(model.loggedIn ? "\(model.notepads.count) 个云词本 · 自动保存选择" : "登录墨墨账号后即可使用").font(.system(size: 10)).foregroundStyle(Palette.muted)
            HStack { Spacer(); Button("查看 / 编辑") { model.sheet = .notepad }.disabled(model.selectedPad == .null); Button("新建") { model.createNotebook() } }.buttonStyle(MemoButtonStyle(compact: true))
        }.foregroundStyle(Palette.deep).padding(13).background(Palette.wash, in: RoundedRectangle(cornerRadius: 11)).overlay(RoundedRectangle(cornerRadius: 11).stroke(Palette.border))
    }
}

extension J { var idText: String { self["id"].text } }

private struct MainTitlebar: View {
    @ObservedObject var model: AppModel
    @ObservedObject var preferences: Preferences
    var compact: Bool
    var body: some View {
        HStack(spacing: compact ? 7 : 12) {
            Color.clear.frame(width: 62).allowsHitTesting(false)
            HStack(spacing: 7) {
                OwlImage().frame(width: 26, height: 26)
                if !compact { Text("墨墨搜词").font(.headline).fixedSize() }
            }.help("墨墨搜词")
            HStack(spacing: 3) {
                titleButton("chevron.left", label: "后退") { model.move(-1) }.disabled(!model.canGoBack)
                titleButton("chevron.right", label: "前进") { model.move(1) }.disabled(!model.canGoForward)
            }
            GeometryReader { geometry in
                HStack(spacing: 5) {
                    if geometry.size.width >= 210 { Text("顽固单词").font(.system(size: 10)).foregroundStyle(Palette.muted) }
                    Button(model.stubbornCurrent.isEmpty ? (model.loggedIn ? (model.stubbornLoading ? "获取中…" : model.stubbornHint.isEmpty ? "暂无" : "查看") : "登录") : model.stubbornCurrent) {
                        if !model.loggedIn { model.openAccountSettings() } else if !model.stubbornCurrent.isEmpty { model.lookup(model.stubbornCurrent) } else { model.sheet = .stubborn }
                    }.font(.system(size: 15, weight: .semibold)).lineLimit(1).truncationMode(.tail).frame(maxWidth: .infinity, alignment: .leading).buttonStyle(.plain).help(model.stubbornCurrent)
                    Button("全部") { if model.loggedIn { model.sheet = .stubborn } else { model.openAccountSettings() } }.font(.system(size: 10)).buttonStyle(.plain)
                    titleButton(preferences.stubbornSort == "random" ? "shuffle" : "chevron.right", label: "下一个顽固单词") { model.nextStubborn() }.disabled(model.stubbornOrder.count < 2)
                }.padding(.horizontal, 8).frame(width: min(264, geometry.size.width), height: 32)
                    .background(Palette.wash, in: RoundedRectangle(cornerRadius: 8))
                    .position(x: min(264, geometry.size.width) / 2, y: geometry.size.height / 2)
            }.frame(minWidth: 105).tourTarget("stubborn")

            if preferences.showCountdown {
                TimelineView(.periodic(from: Date(), by: 30)) { context in countdown(at: context.date) }
                    .fixedSize(horizontal: true, vertical: false).layoutPriority(2)
            }
            Button { model.openAccountSettings() } label: {
                Label { Text(model.titleAccount).lineLimit(1).frame(maxWidth: compact ? 95 : 135) }
                    icon: { Image(systemName: model.activeAccount?.needsLogin == true ? "person.crop.circle.badge.exclamationmark" : "person.crop.circle") }
            }.buttonStyle(.borderless).fixedSize(horizontal: true, vertical: false).layoutPriority(2)
                .help(model.titleAccount + " · " + model.accountStatus)
            HStack(spacing: compact ? 0 : 4) {
                titleButton("gearshape", label: "设置") { model.sheet = .settings }
                titleButton(preferences.alwaysOnTop ? "pin.fill" : "pin", label: "窗口置顶") {
                    preferences.alwaysOnTop.toggle()
                    model.applySystemPreferences?()
                }
            }
        }.padding(.horizontal, compact ? 10 : 14).frame(height: 49).background(.bar).foregroundStyle(Palette.ink)
    }
    private func countdown(at date: Date) -> some View {
        let calendar = Calendar.current
        let days = max(0, calendar.dateComponents([.day], from: calendar.startOfDay(for: date), to: calendar.startOfDay(for: preferences.examDate)).day ?? 0)
        let name = preferences.examName.trimmed.isEmpty ? "考试" : preferences.examName.trimmed
        let visibleName = name.count > (compact ? 9 : 16) ? String(name.prefix(compact ? 9 : 16)) + "…" : name
        return Text("距 \(visibleName) 还有\(days)天")
            .font(.caption.weight(.medium)).monospacedDigit().foregroundStyle(.red)
            .lineLimit(1).help("距 \(name) 还有\(days)天")
    }
    private func titleButton(_ image: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: image).font(.system(size: 13, weight: .medium))
                .frame(width: compact ? 24 : 29, height: 28).contentShape(Rectangle())
        }.buttonStyle(.borderless).help(label).accessibilityLabel(label)
    }
}

private struct WordPage: View {
    @ObservedObject var model: AppModel
    var compact: Bool
    var body: some View {
        ScrollView {
            Card(padding: compact ? 20 : 28) {
                VStack(alignment: .leading, spacing: 0) {
                    HStack(alignment: .center, spacing: 15) {
                        Text(model.word.isEmpty ? "Welcome" : model.word).font(.system(size: compact ? 32 : 42, weight: .semibold)).lineLimit(2).frame(maxWidth: .infinity, alignment: .leading).textSelection(.enabled)
                        if compact { VStack(spacing: 6) { actions } } else { HStack(spacing: 8) { actions } }
                    }
                    HStack(spacing: 8) {
                        Text(model.entry["phonetic"].text.isEmpty ? (model.word.isEmpty ? "你的桌面英汉词典" : "") : "/\(model.entry["phonetic"].text)/").font(.system(size: 16)).foregroundStyle(Palette.muted)
                        Button { model.speak() } label: { Label("发音", systemImage: "speaker.wave.2") }.disabled(model.word.isEmpty)
                        Button("海词词典") { model.openHaici?(model.word) }.disabled(model.word.isEmpty)
                    }.buttonStyle(MemoButtonStyle(compact: true)).padding(.top, 8).padding(.bottom, 14)
                    HStack(spacing: 5) { ForEach(model.entry["tag"].text.split(separator: " ").map(String.init), id: \.self) { Text($0.replacingOccurrences(of: "cet", with: "CET-")).font(.system(size: 10)).padding(.horizontal, 7).padding(.vertical, 4).background(Palette.wash, in: Capsule()).foregroundStyle(Palette.deep) } }
                    Text(model.word.isEmpty ? "输入一个英文单词，开始查词。" : "内置词库 · 离线可用" + (model.entry["bnc"].text.isEmpty ? "" : "  /  BNC 词频 #\(model.entry["bnc"].text)")).font(.system(size: 10)).foregroundStyle(Palette.muted).padding(.top, 8)
                    rule
                    Text("中文释义").sectionLabel().padding(.bottom, 8)
                    LinkedText(text: model.word.isEmpty ? "在左侧输入单词，即可查看释义。" : (model.entry == .null ? "本地词库暂无释义，可打开海词词典查看。" : model.entry["translation"].text.decodedLines), size: 17)
                    if !model.entry["exchange"].text.isEmpty { ExchangeView(exchange: model.entry["exchange"].text, word: model.word).padding(.top, 20) }
                    RelationsView(model: model).padding(.top, 16)
                    rule
                    HStack { HStack(spacing: 10) { Text("记").bold().foregroundStyle(Palette.deep).frame(width: 28, height: 28).background(Palette.wash, in: RoundedRectangle(cornerRadius: 7)); Text("我的助记").font(.system(size: 17, weight: .semibold)) }; Spacer(); Button("编辑助记") { model.sheet = .mnemonic }.buttonStyle(MemoButtonStyle(compact: true)).disabled(model.word.isEmpty) }
                    Text(model.notes.contains { $0["origin"].text == "cloud" } ? "云端助记 · 已缓存到本机" : "本地助记 · 支持编辑").font(.system(size: 11)).foregroundStyle(Palette.muted).padding(.vertical, 10)
                    if model.notes.isEmpty { Text(model.word.isEmpty ? "查词后查看助记" : "暂无助记，点击“编辑助记”添加。").font(.system(size: 12)).foregroundStyle(Palette.muted) }
                    ForEach(Array(model.notes.enumerated()), id: \.offset) { _, note in VStack(alignment: .leading, spacing: 9) { Text(note["noteType"].text).font(.system(size: 11, weight: .semibold)).foregroundStyle(Palette.deep); LinkedText(text: note["text"].text, size: 15, color: Palette.deep) }.padding(18).frame(maxWidth: .infinity, alignment: .leading).background(Palette.wash, in: RoundedRectangle(cornerRadius: 10)).padding(.bottom, 8) }
                    rule
                    HStack { Text("真题例句").font(.system(size: 17, weight: .semibold)); Spacer(); Picker("", selection: $model.examplePaper) { Text("全部考试").tag("ALL"); Text("考研").tag("POSTGRADUATE"); Text("大学四级").tag("CET4"); Text("大学六级").tag("CET6"); Text("高考").tag("COLLEGE_ENTRANCE_EXAM") }.labelsHidden().frame(width: 137).onChange(of: model.examplePaper) { _ in Task { await model.loadExamples(reset: true) } } }
                    Text("点击英文单词可查看释义").font(.system(size: 11)).foregroundStyle(Palette.muted).padding(.top, 8).padding(.bottom, 12)
                    ExamplesView(model: model)
                }
            }.padding(.trailing, 5)
        }
    }
    @ViewBuilder private var actions: some View {
        Button(model.membership ? "✓ 已加入云词本" : "＋ 加入云词本") { Task { await model.toggleMembership() } }.buttonStyle(MemoButtonStyle(accent: true)).disabled(model.word.isEmpty || model.writing)
        Button(todayLabel) { model.addToday() }.buttonStyle(MemoButtonStyle()).disabled(model.word.isEmpty || model.writing || ["AlreadyLearned", "AlreadyToday", "Planned", "Added"].contains(model.todayStatus)).tourTarget("today")
    }
    private var todayLabel: String { switch model.todayStatus { case "AlreadyLearned": return "已学过的单词"; case "AlreadyToday", "Planned", "Added": return "已计划新学"; default: return "＋ 今日新学" } }
    private var rule: some View { Divider().overlay(Palette.border).padding(.vertical, 23) }
}

private struct ExchangeView: View {
    var exchange: String
    var word: String
    var body: some View {
        let labels = ["p": "过去式", "d": "过去分词", "i": "现在分词", "3": "第三人称单数", "s": "复数", "r": "比较级", "t": "最高级", "0": "原形"]
        let lines = exchange.split(separator: "/").compactMap { piece -> String? in let values = piece.split(separator: ":", maxSplits: 1); guard values.count == 2, let label = labels[String(values[0])] else { return nil }; return label + "  " + String(values[1]) }
        LinkedText(text: lines.joined(separator: "    "), size: 12, color: Palette.deep).padding(12).background(Palette.wash, in: RoundedRectangle(cornerRadius: 9))
    }
}

private struct RelationsView: View {
    @ObservedObject var model: AppModel
    private let groups = [("synonyms", "近义词"), ("similar", "形近词"), ("derivatives", "派生词"), ("phrases", "常用短语")]
    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 6) { ForEach(groups, id: \.0) { key, title in relationButton(key, title) } }
                LazyVGrid(columns: [GridItem(.flexible(), alignment: .leading), GridItem(.flexible(), alignment: .leading)], alignment: .leading, spacing: 6) { ForEach(groups, id: \.0) { key, title in relationButton(key, title) } }
            }
            ForEach(groups, id: \.0) { key, title in if model.expandedRelations.contains(key) {
                VStack(alignment: .leading, spacing: 10) { Text(title).font(.system(size: 11, weight: .semibold)); if model.relations[key].list.isEmpty { Text("暂无相关词条").foregroundStyle(Palette.muted) }; ForEach(model.relations[key].list, id: \.wordText) { related in LinkedText(text: related["word"].text + "  " + related["meaning"].text.decodedLines, size: 14) } }.padding(13).frame(maxWidth: .infinity, alignment: .leading).background(Palette.wash, in: RoundedRectangle(cornerRadius: 9))
            } }
        }
    }
    private func relationButton(_ key: String, _ title: String) -> some View {
        Button { if model.expandedRelations.contains(key) { model.expandedRelations.remove(key) } else { model.expandedRelations.insert(key) } } label: { HStack(spacing: 6) { Text("\(title) · \(model.relations[key].list.count)"); Image(systemName: model.expandedRelations.contains(key) ? "chevron.up" : "chevron.down").font(.system(size: 8)) } }.buttonStyle(MemoButtonStyle(compact: true)).fixedSize()
    }
}
extension J { var wordText: String { self["word"].text } }

private struct ExamplesView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if !model.examples.isEmpty { Text("已显示 \(model.examples.count) 条 · 共 \(model.exampleTotal) 条相关结果").font(.system(size: 11)).foregroundStyle(Palette.muted) }
            ForEach(Array(model.examples.enumerated()), id: \.offset) { _, item in VStack(alignment: .leading, spacing: 12) {
                Text(item["source"].text).font(.system(size: 11)).foregroundStyle(Palette.muted)
                LinkedText(text: item["english"].text, highlights: item["matchedWords"].list.map(\.text), size: 15, color: Palette.deep)
                if !item["translation"].text.isEmpty { LinkedText(text: item["translation"].text, chineseHighlights: item["translationTerms"].list.map(\.text), size: 13, color: Palette.muted) }
                if !item["contextEnglish"].text.isEmpty { DisclosureGroup("查看题目") { LinkedText(text: item["contextEnglish"].text, size: 13); LinkedText(text: item["contextTranslation"].text, size: 12, color: Palette.muted) }.font(.system(size: 11)) }
            }.padding(18).frame(maxWidth: .infinity, alignment: .leading).overlay(RoundedRectangle(cornerRadius: 11).stroke(Palette.border)) }
            if model.examplesLoading { ProgressView().controlSize(.small).frame(maxWidth: .infinity) }
            else if !model.examplesError.isEmpty { Text(model.examplesError).font(.system(size: 12)).foregroundStyle(Palette.muted); Button("重试") { Task { await model.loadExamples(reset: model.examples.isEmpty) } }.buttonStyle(MemoButtonStyle()) }
            else if model.exampleOffset < model.exampleTotal { Button("加载更多例句") { Task { await model.loadExamples(reset: false) } }.buttonStyle(MemoButtonStyle()).frame(maxWidth: .infinity) }
            else if model.examples.isEmpty { Text(model.word.isEmpty ? "查词后载入相关真题" : "暂未找到相关例句，可切换考试类型再试。").font(.system(size: 11)).foregroundStyle(Palette.muted) }
        }
    }
}

private struct ChinesePage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("中文反查 · \(model.search)").font(.system(size: 25, weight: .semibold))
            Text("找到 \(model.chineseTotal) 个相关结果，点击单词查看释义。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            ScrollView { LazyVStack(spacing: 5) { ForEach(model.chineseResults, id: \.wordText) { row in Button { model.lookup(row["word"].text) } label: { HStack(alignment: .top, spacing: 15) { Text(row["word"].text).font(.system(size: 15, weight: .semibold)).foregroundStyle(Palette.deep).frame(width: 138, alignment: .leading); Text(row["meaning"].text.decodedLines).font(.system(size: 12)).lineLimit(3).frame(maxWidth: .infinity, alignment: .leading) }.padding(.horizontal, 14).padding(.vertical, 10).background(Palette.paper, in: RoundedRectangle(cornerRadius: 8)) }.buttonStyle(.plain) }; if model.chineseResults.count < model.chineseTotal { Button("显示更多结果") { Task { await model.moreChinese() } }.buttonStyle(MemoButtonStyle()) } } }
        }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

private struct TranslationPage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack { Text("段落翻译").font(.system(size: 23, weight: .semibold)); Spacer(); Picker("", selection: $model.translationDirection) { Text("英译中").tag("en|zh-CN"); Text("中译英").tag("zh-CN|en") }.labelsHidden().frame(width: 112); Button(model.translating ? "翻译中…" : "翻译") { Task { await model.translate() } }.buttonStyle(MemoButtonStyle(accent: true)).disabled(model.translating) }
            TextEditor(text: $model.translationInput).font(.system(size: 15)).padding(8).frame(height: 140).background(Palette.paper, in: RoundedRectangle(cornerRadius: 10)).overlay(RoundedRectangle(cornerRadius: 10).stroke(Palette.border)).tourTarget("translation")
            Text("\(model.translationProvider) · 点击英文单词可查看释义。").font(.system(size: 11)).foregroundStyle(Palette.muted)
            ScrollView { VStack(spacing: 14) { Card { VStack(alignment: .leading, spacing: 12) { Text("原文").sectionLabel(); LinkedText(text: model.translationSource.isEmpty ? "翻译后在这里阅读原文" : model.translationSource, size: 16) } }; Card { VStack(alignment: .leading, spacing: 12) { Text("译文").sectionLabel(); LinkedText(text: model.translationOutput.isEmpty ? "译文将在这里显示" : model.translationOutput, size: 16) } } } }
        }
    }
}
