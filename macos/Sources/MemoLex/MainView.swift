import AppKit
import SwiftUI

struct TitlebarMetrics: Equatable {
    var leading: CGFloat = 85
    var center: CGFloat = 20
    var isFullScreen = false
    var controlsHeight: CGFloat { min(30, center * 2) }
    var height: CGFloat { max(36, center * 2 + 3) }
}

struct MainView: View {
    static let minimumSize = NSSize(width: 760, height: 520)
    @ObservedObject var model: AppModel
    @ObservedObject private var preferences: Preferences
    @FocusState private var searchFocused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(model: AppModel) {
        self.model = model
        self.preferences = model.preferences
    }

    var body: some View {
        GeometryReader { geometry in
            let compact = geometry.size.width < 920
            VStack(spacing: 0) {
                MainTitlebar(model: model, preferences: preferences, compact: compact)
                Divider().overlay(Palette.border)
                HStack(spacing: 0) {
                    sidebar(compact: compact, short: geometry.size.height < 620).frame(width: compact ? 220 : 264)
                    Divider().overlay(Palette.border)
                    ZStack {
                        Group {
                            switch model.page {
                            case .word: WordPage(model: model, compact: compact)
                            case .chinese: ChinesePage(model: model)
                            case .translation: TranslationPage(model: model)
                            }
                        }
                        .id(model.page)
                        .transition(.opacity)
                    }
                    .animation(reduceMotion ? nil : .spring(response: 0.32, dampingFraction: 0.9), value: model.page)
                    .padding(compact ? 12 : 16)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .tourTarget("reading")
                }.frame(maxHeight: .infinity)
                Divider().overlay(Palette.border)
                HStack {
                    Text(model.status.statusMessage).lineLimit(1).truncationMode(.tail)
                    Spacer()
                    Text(dictionarySummary)
                }.font(.caption).foregroundStyle(Palette.muted).padding(.horizontal, 18).frame(height: 27).background(Palette.chrome)
            }.background(Palette.canvas).foregroundStyle(Palette.ink)
                .overlayPreferenceValue(TourTargets.self) { anchors in TourOverlay(model: model, anchors: anchors, context: "main") }
        }
        .frame(minWidth: Self.minimumSize.width, minHeight: Self.minimumSize.height)
        .ignoresSafeArea(.container, edges: .top)
        .tint(Palette.accent)
        .toggleStyle(.checkbox)
        .noFocusRing()
        .environment(\.openURL, OpenURLAction { url in
            if url.scheme == "memolex", let query = url.path.removingPercentEncoding { model.lookup(String(query.dropFirst())); return .handled }
            return .systemAction
        })
        .sheet(item: dialog) { sheet in
            switch sheet {
            case .settings: EmptyView()
            case .history: HistoryView(model: model)
            case .stubborn: StubbornView(model: model)
            case .mnemonic: MnemonicView(model: model)
            case .notepad: NotebookView(model: model)
            case .todayPlan: TodayStudyView(model: model)
            case .dashboard: LearningDashboardView(model: model)
            case .contextReview: ContextReviewSheet(model: model)
            case .morphemePractice:
                if let group = model.morphemePracticeGroup {
                    MorphemeGroupPracticeView(model: model, group: group)
                } else {
                    EmptyView()
                }
            }
        }
        .onAppear { model.focusSearch = { searchFocused = true } }
    }
    private var dialog: Binding<AppSheet?> {
        Binding(get: { model.sheet == .settings ? nil : model.sheet }, set: { sheet in
            if model.sheet != .settings { model.sheet = sheet }
        })
    }
    private var dictionarySummary: String {
        guard model.ready else { return "正在准备词典…" }
        return "内置词库 · \(model.wordCount.formatted()) 词"
    }
    private func sidebar(compact: Bool, short: Bool) -> some View {
        VStack(spacing: short ? 8 : 13) {
            VStack(spacing: short ? 7 : 10) {
                HStack(spacing: 6) {
                    Text("快速查词").sectionLabel()
                    if let shortcut = preferences.shortcuts["search"]?.label, !shortcut.isEmpty {
                        Text(shortcut)
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundStyle(Palette.deep)
                            .padding(.horizontal, 5).padding(.vertical, 1.5)
                            .background(Palette.wash, in: RoundedRectangle(cornerRadius: 5, style: .continuous))
                    }
                    Spacer(minLength: 4)
                    Button { model.toggleRecallMode() } label: {
                        HStack(spacing: 4) {
                            Circle()
                                .fill(preferences.recallModeEnabled ? Palette.accent : Palette.muted.opacity(0.55))
                                .frame(width: 6, height: 6)
                            Text("先回想")
                                .font(.system(size: 10.5, weight: preferences.recallModeEnabled ? .semibold : .medium))
                                .foregroundStyle(preferences.recallModeEnabled ? Palette.deep : Palette.muted)
                        }
                        .padding(.horizontal, 7).padding(.vertical, 2.5)
                        .background(preferences.recallModeEnabled ? Palette.wash : Color.clear, in: Capsule())
                        .overlay(Capsule().strokeBorder(preferences.recallModeEnabled ? Palette.accent.opacity(0.75) : Palette.border, lineWidth: 0.9))
                    }
                    .buttonStyle(.plain)
                    .noFocusRing()
                    .help(preferences.recallModeEnabled
                          ? "先回想模式（已开启）：再次查询已查过的单词时会先遮住释义 2 秒，点击可关闭"
                          : "先回想模式（已关闭）：开启后，再次查询已查过的单词时会先遮住释义 2 秒，帮你主动回忆")
                }
                HStack(spacing: 6) {
                    HStack(spacing: 7) {
                        Image(systemName: "magnifyingglass").font(.system(size: 12.5)).foregroundStyle(Palette.muted)
                        TextField("输入英文或中文", text: $model.search).textFieldStyle(.plain)
                            .help("输入英文查释义，或用中文找单词")
                            .focused($searchFocused).onSubmit { model.lookup(model.search) }
                            .onChange(of: model.search) { _ in model.suggest() }
                    }.padding(.horizontal, 10).frame(height: 36).controlSurface()
                    Button { model.lookup(model.search) } label: {
                        Image(systemName: "magnifyingglass").font(.system(size: 13, weight: .semibold))
                    }
                    .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                    .frame(width: 36, height: 36)
                    .help("查词").accessibilityLabel("查词")
                }.tourTarget("search")
                HStack(spacing: 7) {
                    Button("段落翻译") { model.enterTranslation() }
                    Button("截图翻译") { model.captureScreen?() }.tourTarget("screenshot")
                    Button("今日学习") {
                        if model.loggedIn { model.sheet = .todayPlan }
                        else { model.openAccountSettings() }
                    }.tourTarget("today-plan")
                }.buttonStyle(LookupButtonStyle(small: true, expands: true))
            }
            SidebarSplit {
                VStack(alignment: .leading, spacing: 7) {
                    Text("单词建议").sectionLabel()
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 1) {
                            ForEach(model.suggestions, id: \.self) { w in
                                let count = model.lookupCount(for: w)
                                Button { model.lookup(w, keepSearchQuery: true) } label: {
                                    HStack(spacing: 6) {
                                        Text(w).font(.system(size: 14)).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
                                        if count >= 2 {
                                            Text("\(count)次")
                                                .font(.system(size: 10, weight: .medium))
                                                .foregroundStyle(Color(red: 0.65, green: 0.34, blue: 0.03))
                                                .padding(.horizontal, 5).padding(.vertical, 1.5)
                                                .background(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.14), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
                                                .overlay(RoundedRectangle(cornerRadius: 5, style: .continuous).strokeBorder(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.32), lineWidth: 0.7))
                                        }
                                    }
                                }
                                .buttonStyle(SidebarRowStyle(selected: w == model.word))
                            }
                        }.background(SidebarWheelPaging())
                    }
                }
            } bottom: {
                VStack(alignment: .leading, spacing: 7) {
                    HStack { Text("最近查词").sectionLabel(); Spacer(); Button("全部 \(model.history.count) 条") { model.sheet = .history }.buttonStyle(.plain).font(.system(size: 10)).foregroundStyle(Palette.deep) }
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 1) {
                            if model.history.isEmpty { Text("查过的单词会出现在这里").font(.system(size: 11)).foregroundStyle(Palette.muted).padding(9) }
                            ForEach(model.history.prefix(60)) { record in
                                Button { model.lookup(record.word) } label: {
                                    HStack {
                                        Text(record.word).lineLimit(1)
                                        Spacer()
                                        Image(systemName: "arrow.right").font(.system(size: 9)).foregroundStyle(Palette.muted)
                                    }.font(.system(size: 14)).contentShape(Rectangle())
                                }.buttonStyle(SidebarRowStyle(selected: record.word == model.word))
                            }
                        }.background(SidebarWheelPaging())
                    }
                }
            }
            cloudCard(short: short).tourTarget("cloud")
        }.padding(.horizontal, compact ? 14 : 17).padding(.vertical, short ? 9 : 15).background(Palette.sidebar)
    }
    private func cloudCard(short: Bool) -> some View {
        VStack(alignment: .leading, spacing: short ? 6 : 10) {
            HStack { Text("我的云词本").font(.system(size: 12, weight: .semibold)); Spacer(); Button { Task { await model.refreshNotepads() } } label: { Image(systemName: "arrow.clockwise") }.buttonStyle(.plain) }
            MemoPicker(title: "选择云词本", selection: notepadSelection,
                       options: model.notepads.map { ($0["id"].text, $0["title"].text) }).disabled(!model.loggedIn)
            if let hint = model.notepadConnectionHint { Text(hint).font(.caption).foregroundStyle(Palette.muted) }
            HStack { Spacer(); Button("查看 / 编辑") { model.sheet = .notepad }.disabled(model.selectedPad == .null); Button("新建") { model.createNotebook() } }.buttonStyle(MemoButtonStyle(compact: true))
        }.foregroundStyle(Palette.ink).padding(short ? 9 : 12).background(Palette.wash, in: RoundedRectangle(cornerRadius: 12)).overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Palette.border, lineWidth: 0.6).allowsHitTesting(false))
    }

    private var notepadSelection: Binding<String> {
        Binding(get: { model.preferences.selectedNotepad }, set: { id in
            Task<Void, Never> { await model.selectNotepad(id) }
        })
    }
}

private struct SidebarSplit<Top: View, Bottom: View>: View {
    private let top: Top
    private let bottom: Bottom
    @State private var topShare: CGFloat = 0.5
    @State private var hovered = false
    @GestureState private var dragOffset: CGFloat = 0

    private let gripHeight: CGFloat = 20
    private let minimumPaneHeight = SidebarRowStyle.height + 24

    init(@ViewBuilder top: () -> Top, @ViewBuilder bottom: () -> Bottom) {
        self.top = top()
        self.bottom = bottom()
    }

    var body: some View {
        GeometryReader { geometry in
            let available = max(2 * minimumPaneHeight, geometry.size.height - gripHeight)
            let start = boundedHeight(topShare * available, available: available)
            let topHeight = boundedHeight(start + dragOffset, available: available)

            VStack(spacing: 0) {
                top.frame(height: topHeight, alignment: .top).clipped()
                Capsule()
                    .fill(hovered || dragOffset != 0 ? Palette.deep : Palette.splitGrip)
                    .frame(width: 30, height: 3)
                    .frame(maxWidth: .infinity)
                    .frame(height: gripHeight)
                    .contentShape(Rectangle())
                    .onHover { inside in
                        hovered = inside
                        (inside ? NSCursor.resizeUpDown : NSCursor.arrow).set()
                    }
                    .gesture(DragGesture(minimumDistance: 1, coordinateSpace: .named("sidebarSplit"))
                        .updating($dragOffset) { drag, offset, _ in offset = drag.translation.height }
                        .onEnded { drag in
                            topShare = boundedHeight(start + drag.translation.height, available: available) / available
                        })
                    .accessibilityLabel("调整单词建议和最近查词的高度")
                    .accessibilityValue("\(Int(100 * topHeight / available))%")
                    .accessibilityAdjustableAction { direction in
                        switch direction {
                        case .increment: topShare = boundedHeight(start + 20, available: available) / available
                        case .decrement: topShare = boundedHeight(start - 20, available: available) / available
                        @unknown default: break
                        }
                    }
                bottom.frame(height: available - topHeight, alignment: .top).clipped()
            }
            .coordinateSpace(name: "sidebarSplit")
        }
        .frame(minHeight: 2 * minimumPaneHeight + gripHeight)
    }

    private func boundedHeight(_ height: CGFloat, available: CGFloat) -> CGFloat {
        min(max(height, minimumPaneHeight), available - minimumPaneHeight)
    }
}

private struct SidebarWheelPaging: NSViewRepresentable {
    func makeNSView(context: Context) -> PagingView { PagingView() }
    func updateNSView(_ view: PagingView, context: Context) {}
    static func dismantleNSView(_ view: PagingView, coordinator: ()) { view.removeMonitor() }

    final class PagingView: NSView {
        private var monitor: Any?

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            removeMonitor()
            guard window != nil else { return }
            monitor = NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak self] event in
                self?.page(event) == true ? nil : event
            }
        }

        func removeMonitor() {
            if let monitor { NSEvent.removeMonitor(monitor) }
            monitor = nil
        }

        private func page(_ event: NSEvent) -> Bool {
            guard !event.hasPreciseScrollingDeltas, event.window === window, event.scrollingDeltaY != 0,
                  abs(event.scrollingDeltaY) > abs(event.scrollingDeltaX),
                  let scroll = enclosingScrollView, let document = scroll.documentView else { return false }
            let clip = scroll.contentView
            let stride = SidebarRowStyle.height + 1
            let height = clip.bounds.height
            let rows = max(1, Int((height + 1) / stride))
            guard height > 0, rows <= 2, document.frame.height > height,
                  clip.bounds.contains(clip.convert(event.locationInWindow, from: nil)) else { return false }
            let direction: CGFloat = event.scrollingDeltaY > 0 ? -1 : 1
            var target = clip.bounds
            target.origin.y += CGFloat(rows) * stride * direction * (clip.isFlipped ? 1 : -1)
            clip.scroll(to: clip.constrainBoundsRect(target).origin)
            scroll.reflectScrolledClipView(clip)
            return true
        }
    }
}

extension J { var idText: String { self["id"].text } }

private struct MainTitlebar: View {
    @ObservedObject var model: AppModel
    @ObservedObject var preferences: Preferences
    var compact: Bool
    @FocusState private var pinFocused: Bool
    var body: some View {
        let metrics = model.titlebar
        HStack(spacing: compact ? 5 : 8) {
            Color.clear.frame(width: metrics.leading).allowsHitTesting(false)
            if !metrics.isFullScreen {
                pinButton
            }
            HStack(spacing: 0) {
                titleButton("chevron.left", label: "后退") { model.move(-1) }.disabled(!model.canGoBack)
                titleButton("chevron.right", label: "前进") { model.move(1) }.disabled(!model.canGoForward)
            }
            stubbornBar.frame(minWidth: 168, maxWidth: compact ? 250 : 300).layoutPriority(1).tourTarget("stubborn")
            Spacer(minLength: 0)
            if preferences.showCountdown {
                TimelineView(.periodic(from: Date(), by: 30)) { context in countdown(at: context.date) }
                    .fixedSize(horizontal: true, vertical: false).layoutPriority(2)
            }
            Button { model.openAccountSettings() } label: {
                HStack(spacing: 6) {
                    Circle()
                        .fill(accountDotColor)
                        .frame(width: 7, height: 7)
                    Text(model.titleAccount).font(.system(size: 12, weight: .semibold)).lineLimit(1).frame(maxWidth: compact ? 108 : 132, alignment: .leading)
                }
            }.buttonStyle(PillButtonStyle()).fixedSize(horizontal: true, vertical: false).layoutPriority(2)
                .help(model.titleAccount + " · " + model.accountStatus)
            titleButton("chart.bar", label: "学习统计看板") { model.sheet = .dashboard }
            titleButton("gearshape", label: "设置") { model.sheet = .settings }
            HStack(spacing: 7) {
                OwlImage().frame(width: 28, height: 28).accessibilityHidden(true)
                if !compact {
                    VStack(alignment: .leading, spacing: 1) {
                        Text("墨墨搜词").font(.system(size: 13.5, weight: .bold))
                        Text("MemoLex").font(.system(size: 9, weight: .medium)).foregroundStyle(Palette.muted)
                    }
                }
            }.fixedSize().accessibilityElement(children: .combine)
        }.padding(.trailing, compact ? 10 : 14)
            .frame(height: metrics.controlsHeight)
            .padding(.top, max(0, metrics.center - metrics.controlsHeight / 2))
            .frame(height: metrics.height, alignment: .top)
            .background(Palette.chrome).foregroundStyle(Palette.ink)
    }

    private var accountDotColor: Color {
        if model.activeAccount?.needsLogin == true { return Color(red: 0.96, green: 0.62, blue: 0.04) }
        if model.loggedIn { return Color(red: 0.09, green: 0.72, blue: 0.47) }
        return Palette.muted.opacity(0.7)
    }

    private var stubbornBar: some View {
        HStack(spacing: compact ? 5 : 8) {
            if !compact && !model.stubbornCurrent.isEmpty {
                Button("顽固") { model.sheet = .stubborn }
                    .font(.system(size: 10.5, weight: .bold))
                    .foregroundStyle(Palette.deep)
                    .padding(.horizontal, 7).padding(.vertical, 2.5)
                    .background(Palette.paper, in: Capsule())
                    .overlay(Capsule().strokeBorder(Palette.accent.opacity(0.32), lineWidth: 0.75))
                    .buttonStyle(.plain)
                    .fixedSize()
            }
            Button(model.stubbornCurrent.isEmpty ? (model.stubbornLoading ? "刷新中…" : "顽固单词") : model.stubbornCurrent) {
                if !model.stubbornCurrent.isEmpty { model.lookup(model.stubbornCurrent) }
                else { model.sheet = .stubborn }
            }.font(.system(size: model.stubbornCurrent.isEmpty ? 12 : 14.5, weight: .bold))
                .lineLimit(1).frame(maxWidth: .infinity, alignment: .leading).buttonStyle(.plain)
                .help(model.stubbornCurrent.isEmpty ? "顽固单词" : model.stubbornCurrent)
            Button(stubbornListLabel) {
                model.sheet = .stubborn
            }
                .font(.system(size: 11.5, weight: .semibold)).buttonStyle(.plain).fixedSize().foregroundStyle(Palette.deep)
                .help("查看全部顽固单词")
            titleButton(preferences.stubbornSort == "random" ? "shuffle" : "arrow.right", label: "下一个顽固单词") { model.nextStubborn() }
                .disabled(model.stubbornOrder.count < 2)
        }.padding(.leading, 8).padding(.trailing, 2).frame(height: 29)
            .background(Palette.wash, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.accent.opacity(0.24), lineWidth: 0.75))
    }
    private var stubbornListLabel: String {
        if model.stubbornLoading && !model.stubbornCurrent.isEmpty { return "刷新中…" }
        if model.showingCachedStubborn { return "全部 \(model.stubbornWords.count)" }
        if model.canBrowseStubborn { return "全部 \(model.stubbornTotal)" }
        return "全部"
    }

    private func countdown(at date: Date) -> some View {
        let calendar = Calendar.current
        let days = max(0, calendar.dateComponents([.day], from: calendar.startOfDay(for: date), to: calendar.startOfDay(for: preferences.examDate)).day ?? 0)
        let name = preferences.examName.trimmed.isEmpty ? "考试" : preferences.examName.trimmed
        let limit = compact ? 5 : 12
        let visibleName = name.count > limit ? String(name.prefix(limit)) + "…" : name
        let labelText = compact ? "\(visibleName) \(days)天" : "距 \(visibleName) 还有 \(days) 天"
        return Text(labelText)
            .font(.system(size: 11.5, weight: .semibold)).monospacedDigit()
            .foregroundStyle(Color(red: 0.78, green: 0.14, blue: 0.25))
            .padding(.horizontal, compact ? 8 : 9).padding(.vertical, compact ? 3.5 : 4)
            .background(Color(red: 0.96, green: 0.25, blue: 0.37).opacity(0.11), in: Capsule())
            .overlay(Capsule().strokeBorder(Color(red: 0.96, green: 0.25, blue: 0.37).opacity(0.28), lineWidth: 0.8))
            .lineLimit(1).help("距 \(name) 还有\(days)天")
    }
    @ViewBuilder private var pinButton: some View {
        let selected = preferences.alwaysOnTop
        let button = Button {
            preferences.alwaysOnTop.toggle()
            model.applySystemPreferences?()
        } label: {
            Image(systemName: selected ? "pin.fill" : "pin")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(selected ? Palette.deep : Palette.ink)
                .frame(width: 24, height: 24)
                .background(selected || pinFocused ? Palette.wash : Color.clear, in: Circle())
                .frame(width: compact ? 25 : 29, height: 28)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain).focused($pinFocused)
        .help("窗口置顶").accessibilityLabel("窗口置顶").accessibilityAddTraits(selected ? .isSelected : [])
        if #available(macOS 14.0, *) { button.focusEffectDisabled() }
        else { button.clipShape(RoundedRectangle(cornerRadius: 8)) }
    }
    private func titleButton(_ image: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: image).font(.system(size: 13, weight: .medium))
                .frame(width: compact ? 25 : 29, height: 28).contentShape(Rectangle())
        }.buttonStyle(.borderless).foregroundStyle(Palette.ink)
         .help(label).accessibilityLabel(label)
    }
}

private struct WordPage: View {
    @ObservedObject var model: AppModel
    var compact: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private static let tagNames = ["zk": "中考", "gk": "高考", "cet4": "CET-4", "cet6": "CET-6",
                                   "ky": "考研", "toefl": "TOEFL", "ielts": "IELTS", "gre": "GRE"]
    var body: some View {
        ScrollView {
            Card(padding: compact ? 16 : 22) {
                VStack(alignment: .leading, spacing: 0) {
                    HStack(alignment: .center, spacing: 14) {
                        ZStack(alignment: .leading) {
                            Text(model.word.isEmpty ? "查词" : model.word)
                                .font(.system(size: compact ? 29 : 34, weight: .bold))
                                .lineLimit(2)
                                .textSelection(.enabled)
                                .id(model.word)
                                .transition(.opacity)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .animation(reduceMotion ? nil : .spring(response: 0.3, dampingFraction: 0.9), value: model.word)
                        if compact { VStack(spacing: 5) { actions } } else { HStack(spacing: 7) { actions } }
                    }
                    HStack(spacing: 7) {
                        Text(model.entry["phonetic"].text.isEmpty ? (model.word.isEmpty ? "你的桌面英汉词典" : "") : "/\(model.entry["phonetic"].text)/").font(.system(size: 14.5)).foregroundStyle(Palette.muted)
                        ForEach(EnglishAccent.allCases, id: \.self) { accent in
                            Button { model.speak(accent) } label: { Label(accent.label, systemImage: "speaker.wave.2") }
                                .disabled(model.word.isEmpty)
                                .help("播放\(accent.label)")
                        }
                        Button("海词词典") { model.openHaici?(model.word) }.disabled(model.word.isEmpty)
                    }.buttonStyle(PillButtonStyle()).padding(.top, 6).padding(.bottom, 7)
                    HStack(alignment: .center, spacing: 8) {
                        wordTags
                        definitionSource.font(.system(size: 10.5))
                    }
                    if model.entry["translation"].text.trimmed.isEmpty && !model.spellingSuggestions.isEmpty {
                        VStack(alignment: .leading, spacing: 6) {
                            Text("未找到精确匹配，你是不是要查：")
                                .font(.system(size: 11.5, weight: .semibold))
                                .foregroundStyle(Palette.deep)
                            MorphemeFlowLayout(spacing: 6, lineSpacing: 5, expandLast: false) {
                                ForEach(model.spellingSuggestions, id: \.self) { candidate in
                                    Button(candidate) { model.lookup(candidate) }
                                        .buttonStyle(PillButtonStyle())
                                }
                            }
                        }
                        .padding(10)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Palette.wash.opacity(0.7), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.accent.opacity(0.28), lineWidth: 0.8))
                        .padding(.top, 8)
                    }
                    if !model.recallMode && !model.word.isEmpty && model.currentWordLookupCount >= 3 && !model.membership {
                        HStack(spacing: 8) {
                            Text("已查 \(model.currentWordLookupCount) 次 · 容易忘记，建议加入云词本巩固")
                                .font(.system(size: 11.5, weight: .medium))
                                .foregroundStyle(Color(red: 0.65, green: 0.34, blue: 0.03))
                                .fixedSize(horizontal: false, vertical: true)
                            Spacer(minLength: 8)
                            Button("先回想") { model.startRecallMode() }
                                .buttonStyle(MemoButtonStyle(compact: true))
                                .help("先遮住释义，回想 2 秒再看答案")
                        }
                        .padding(.horizontal, 11).padding(.vertical, 5)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.12), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.32), lineWidth: 0.8))
                        .padding(.top, 7)
                    }
                    if let ctx = model.pendingContext,
                       !model.word.isEmpty,
                       ctx.word.caseInsensitiveCompare(model.word) == .orderedSame {
                        let saved = model.isContextSentenceSaved(word: model.word, sentence: ctx.sentence)
                        HStack(alignment: .center, spacing: 10) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("来自「\(ctx.source)」")
                                    .font(.system(size: 10.5, weight: .semibold))
                                    .foregroundStyle(Palette.deep)
                                Text(ctx.sentence)
                                    .font(.system(size: 12.5))
                                    .foregroundStyle(Palette.ink)
                                    .lineLimit(2)
                            }
                            Spacer(minLength: 8)
                            Button(saved ? "✓ 已收藏 · 去自测" : "＋ 收藏例句") {
                                if saved {
                                    model.openContextReview(focusWord: model.word)
                                } else {
                                    Task {
                                        await model.saveContextSentence(
                                            word: model.word,
                                            sentence: ctx.sentence,
                                            translation: ctx.translation,
                                            source: ctx.source,
                                            matchedForm: ctx.matchedForm
                                        )
                                    }
                                }
                            }
                            .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                        }
                        .padding(.horizontal, 11).padding(.vertical, 7)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Palette.wash.opacity(0.72), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.accent.opacity(0.3), lineWidth: 0.85))
                        .padding(.top, 8)
                    }
                    rule
                    if model.recallMode {
                        recallMaskCard
                    } else {
                    Text("中文释义").sectionLabel().padding(.bottom, 4)
                    LinkedText(text: definitionText, size: 16, highlightPartOfSpeech: !model.word.isEmpty)
                    if !model.entry["exchange"].text.isEmpty { ExchangeView(exchange: model.entry["exchange"].text, word: model.word).padding(.top, 10) }
                    RelationsView(model: model).padding(.top, 10)
                    rule
                    HStack {
                        HStack(spacing: 8) {
                            Text("记").font(.system(size: 12, weight: .bold)).foregroundStyle(Palette.deep).frame(width: 24, height: 24)
                                .background(Palette.wash, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                                .overlay(RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(Palette.accent.opacity(0.24), lineWidth: 0.75))
                            Text("我的助记").font(.system(size: 15.5, weight: .semibold))
                            Text(noteSummary).font(.system(size: 11.5)).foregroundStyle(Palette.muted).lineLimit(1)
                        }
                        Spacer()
                        Button("编辑助记") { model.sheet = .mnemonic }.buttonStyle(PillButtonStyle()).disabled(model.word.isEmpty)
                    }.padding(.bottom, 6)
                    ForEach(Array(model.notes.enumerated()), id: \.offset) { _, note in
                        let breakdowns = note["breakdowns"].list
                        let hasBreakdowns = !breakdowns.isEmpty
                        let remainingText = hasBreakdowns ? note["remainingText"].text : note["text"].text
                        let originText = note["origin"].text == "cloud" ? "云端助记" : "本地助记"
                        VStack(alignment: .leading, spacing: 7) {
                            HStack(alignment: .center, spacing: 8) {
                                Text(hasBreakdowns ? "词根拆解 · \(originText)" : originText)
                                    .font(.system(size: 11, weight: .semibold))
                                    .foregroundStyle(Palette.muted)
                                Spacer()
                                Text(note["noteType"].text)
                                    .font(.system(size: 10.5, weight: .semibold))
                                    .foregroundStyle(Palette.deep)
                                    .padding(.horizontal, 8)
                                    .padding(.vertical, 2)
                                    .background(Palette.accent.opacity(0.18), in: Capsule())
                            }
                            if hasBreakdowns {
                                MnemonicPartsView(
                                    breakdowns: breakdowns,
                                    lookupWord: { model.lookup($0) },
                                    loadFamily: { form, meaning in
                                        await model.lookupMorphemeFamily(form: form, meaning: meaning, excludeWord: model.word)
                                    },
                                    practiceGroup: { form, meaning in
                                        model.openMorphemePractice(form: form, meaning: meaning, currentWord: model.word)
                                    }
                                )
                                    .id("\(model.word)\n\(note["text"].text)")
                            }
                            if !remainingText.trimmed.isEmpty {
                                if hasBreakdowns {
                                    Divider().overlay(Palette.border.opacity(0.75))
                                }
                                LinkedText(
                                    text: remainingText,
                                    size: hasBreakdowns ? 13 : 14.5,
                                    color: Palette.ink,
                                    accentLinks: true,
                                    allowedLinks: Set(note["linkableWords"].list.map { $0.text.lowercased() }),
                                    boldWord: model.word
                                )
                            }
                        }.padding(.horizontal, 13).padding(.vertical, 10).frame(maxWidth: .infinity, alignment: .leading)
                            .background(Palette.wash.opacity(0.75), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
                            .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 1))
                            .padding(.bottom, 6)
                    }
                    savedContextsSection
                    rule
                    HStack {
                        HStack(spacing: 8) {
                            Text("例").font(.system(size: 12, weight: .bold)).foregroundStyle(Palette.deep).frame(width: 24, height: 24)
                                .background(Palette.wash, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                                .overlay(RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(Palette.accent.opacity(0.24), lineWidth: 0.75))
                            Text("真题例句").font(.system(size: 15.5, weight: .semibold))
                            if !model.examples.isEmpty {
                                Text("\(model.examples.count) / \(model.exampleTotal) 条").font(.system(size: 11.5)).foregroundStyle(Palette.muted)
                            }
                        }
                        Spacer()
                        MemoPicker(title: "真题类型", selection: $model.examplePaper, options: [
                            ("ALL", "全部考试"), ("POSTGRADUATE", "考研"), ("CET4", "大学四级"),
                            ("CET6", "大学六级"), ("COLLEGE_ENTRANCE_EXAM", "高考")
                        ], height: 28).frame(width: 120)
                    }.padding(.bottom, 8).onChange(of: model.examplePaper) { _ in Task { await model.loadExamples(reset: true) } }
                    ExamplesView(model: model)
                    }
                }
            }.padding(.trailing, 5)
        }
    }
    private var noteSummary: String {
        if model.word.isEmpty { return "查词后可查看助记" }
        if model.notes.isEmpty { return "暂无助记" }
        let source = model.notes.contains { $0["origin"].text == "cloud" } ? "云端助记" : "本地助记"
        return "\(source) · \(model.notes.count) 条"
    }
    private var definitionText: String {
        if model.word.isEmpty { return "在左侧输入英文或中文查词。" }
        let local = model.entry["translation"].text.decodedLines
        if !local.trimmed.isEmpty { return local }
        if model.definitionLoading { return "正在查找释义…" }
        let online = model.onlineDefinition["meaning"].text
        return online.isEmpty ? "暂无释义。可在海词词典中查看。" : online
    }
    @ViewBuilder private var definitionSource: some View {
        if !model.entry["translation"].text.trimmed.isEmpty {
            let label = Text("内置词库").foregroundStyle(Palette.muted)
            let rank = Int(model.entry["bnc"].text) ?? 0
            if rank > 0 { label.help("BNC 词频排名 \(rank.formatted())") } else { label }
        } else if model.onlineDefinition != .null {
            let label = Text(verbatim: model.onlineDefinition["sourceLabel"].text).foregroundStyle(Palette.muted)
            let attribution = model.onlineDefinition["sourceAttribution"].text
            if attribution.isEmpty { label } else { label.help(attribution) }
        }
    }
    @ViewBuilder private var wordTags: some View {
        let tags = model.entry["tag"].text.split(separator: " ").map(String.init)
        let labels: [String] = {
            var list = tags.map { Self.tagNames[$0] ?? $0.uppercased() }
            if model.entry["oxford"].text == "1" { list.append("核心词汇") }
            if let stars = Int(model.entry["collins"].text), stars > 0 { list.append(String(repeating: "★", count: min(stars, 5))) }
            return list
        }()
        if !labels.isEmpty {
            MorphemeFlowLayout(spacing: 6, lineSpacing: 6, expandLast: false) {
                ForEach(labels, id: \.self) { label in
                    let isCore = label == "核心词汇"
                    let isStar = label.hasPrefix("★")
                    let fg: Color = isCore ? Color(red: 0.06, green: 0.54, blue: 0.36)
                        : isStar ? Color(red: 0.72, green: 0.43, blue: 0.04)
                        : Palette.deep
                    let bg: Color = isCore ? Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.14)
                        : isStar ? Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.15)
                        : Palette.wash
                    let stroke: Color = isCore ? Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.32)
                        : isStar ? Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.35)
                        : Palette.accent.opacity(0.26)
                    Text(label)
                        .font(.system(size: 11, weight: .semibold))
                        .padding(.horizontal, 9).padding(.vertical, 3.5)
                        .background(bg, in: Capsule())
                        .overlay(Capsule().strokeBorder(stroke, lineWidth: 0.75))
                        .foregroundStyle(fg)
                }
            }
        }
    }
    @ViewBuilder private var actions: some View {
        let width: CGFloat? = compact ? 118 : nil
        Button(model.savingCurrentNotepadWord ? "正在保存…" : model.membership ? "✓ 已加入云词本" : "＋ 加入云词本") {
            Task { await model.toggleMembership() }
        }
            .buttonStyle(LookupButtonStyle(primary: true, fixedWidth: width))
            .disabled(model.word.isEmpty || model.writing || model.selectingNotepad || model.savingCurrentNotepadWord)
        Button(todayLabel) { model.addToday() }.buttonStyle(LookupButtonStyle(fixedWidth: width))
            .disabled(model.word.isEmpty || model.writing || ["AlreadyLearned", "AlreadyToday", "Planned", "Added", "Loading", "Missing"].contains(model.todayStatus))
            .help(model.todayStatusError)
            .tourTarget("today")
    }
    private var todayLabel: String {
        switch model.todayStatus {
        case "AlreadyLearned": return "已学过"
        case "AlreadyToday", "Planned", "Added": return "已计划新学"
        case "Loading": return "正在获取学习状态…"
        case "Missing": return "墨墨未收录"
        case "Failed": return "重新获取学习状态"
        case "SyncRequired": return "核对学习记录"
        case "PendingVerification": return "核对今日新学"
        default: return "＋ 今日新学"
        }
    }
    @ViewBuilder private var savedContextsSection: some View {
        let wordContexts = model.contextSentences(for: model.word)
        if !wordContexts.isEmpty {
            rule
            HStack {
                HStack(spacing: 8) {
                    Text("句").font(.system(size: 12, weight: .bold)).foregroundStyle(Palette.deep).frame(width: 24, height: 24)
                        .background(Palette.wash, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(Palette.accent.opacity(0.24), lineWidth: 0.75))
                    Text("我的例句").font(.system(size: 15.5, weight: .semibold))
                    Text("本词 \(wordContexts.count) 条 · 共 \(model.contextSentences.count) 条")
                        .font(.system(size: 11.5)).foregroundStyle(Palette.muted).lineLimit(1)
                }
                Spacer()
                Button("例句自测 (\(model.contextSentences.count))") {
                    model.openContextReview(focusWord: model.word)
                }.buttonStyle(PillButtonStyle())
            }.padding(.bottom, 6)
            ForEach(Array(wordContexts.enumerated()), id: \.offset) { _, ctx in
                let ctxId = ctx["id"].text
                let sentence = ctx["sentence"].text
                let trans = ctx["translation"].text
                let reviews = ctx["reviewCount"].integer
                let sourceMeta = reviews > 0 ? "\(ctx["source"].text) · 已自测 \(reviews) 次" : ctx["source"].text
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text(sourceMeta)
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundStyle(Palette.deep)
                            .padding(.horizontal, 8).padding(.vertical, 2)
                            .background(Palette.paper, in: Capsule())
                            .overlay(Capsule().strokeBorder(Palette.accent.opacity(0.24), lineWidth: 0.75))
                        Spacer()
                        Button("移除") {
                            Task { await model.removeContextSentence(id: ctxId) }
                        }
                        .buttonStyle(.plain)
                        .font(.system(size: 11))
                        .foregroundStyle(Palette.muted)
                    }
                    LinkedText(text: sentence, highlights: [model.word, ctx["matchedForm"].text].filter { !$0.isEmpty }, size: 14, color: Palette.ink)
                    if !trans.isEmpty {
                        Text(trans).font(.system(size: 12.5)).foregroundStyle(Palette.muted).lineSpacing(3)
                    }
                }
                .padding(.horizontal, 13).padding(.vertical, 10).frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.wash.opacity(0.7), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85))
                .padding(.bottom, 6)
            }
        }
    }
    @ViewBuilder private var recallMaskCard: some View {
        let count = model.currentWordLookupCount
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(count >= 2
                     ? "第 \(count) 次查「\(model.word)」，先回想 2 秒"
                     : "先回想 2 秒「\(model.word)」的意思")
                    .font(.system(size: 16, weight: .bold))
                Spacer()
                Text(model.recallReady ? "可以查看了" : "回想 2 秒")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Color(red: 0.65, green: 0.34, blue: 0.03))
                    .padding(.horizontal, 9).padding(.vertical, 3)
                    .background(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.15), in: Capsule())
            }
            Text("先在脑海里想一想它的释义或用法，随时按空格 / 回车查看答案。")
                .font(.system(size: 12.5))
                .foregroundStyle(Palette.muted)
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Palette.paper)
                    Capsule().fill(Palette.accent)
                        .frame(width: max(0, geo.size.width * CGFloat(model.recallProgress)))
                }
            }
            .frame(height: 5)
            HStack(spacing: 10) {
                Button("查看释义") { model.revealFromRecall() }
                    .buttonStyle(MemoButtonStyle(accent: true))
                    .keyboardShortcut(.return, modifiers: [])
                Text("按空格 / 回车立即查看")
                    .font(.system(size: 11))
                    .foregroundStyle(Palette.muted)
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.wash.opacity(0.78), in: RoundedRectangle(cornerRadius: 13, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 13, style: .continuous).strokeBorder(Palette.accent.opacity(0.32), lineWidth: 0.9))
        .overlay {
            Button("") { model.revealFromRecall() }
                .keyboardShortcut(.space, modifiers: [])
                .opacity(0).frame(width: 0, height: 0).allowsHitTesting(false)
        }
    }
    private var rule: some View { Divider().overlay(Palette.border).padding(.vertical, 12) }
}

private struct ExchangeView: View {
    var exchange: String
    var word: String
    @Environment(\.openURL) private var openURL
    var body: some View {
        let labels = ["p": "过去式", "d": "过去分词", "i": "现在分词", "3": "第三人称单数", "s": "复数", "r": "比较级", "t": "最高级", "0": "原形"]
        let items = exchange.split(separator: "/").compactMap { piece -> (String, String)? in
            let values = piece.split(separator: ":", maxSplits: 1)
            guard values.count == 2, let label = labels[String(values[0])] else { return nil }
            let form = String(values[1]).trimmed
            return form.isEmpty ? nil : (label, form)
        }
        if !items.isEmpty {
            MorphemeFlowLayout(spacing: 7, lineSpacing: 7, expandLast: false) {
                ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                    Button {
                        if let encoded = item.1.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed),
                           let url = URL(string: "memolex://word/\(encoded)") {
                            openURL(url)
                        }
                    } label: {
                        HStack(spacing: 5) {
                            Text(item.0).font(.system(size: 11.5)).foregroundStyle(Palette.muted)
                            Text(item.1).font(.system(size: 12.5, weight: .semibold)).foregroundStyle(Palette.deep)
                        }
                        .padding(.horizontal, 10).padding(.vertical, 5)
                        .background(Palette.paper, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85))
                    }.buttonStyle(.plain)
                }
            }
            .padding(11)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Palette.wash.opacity(0.65), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border.opacity(0.8), lineWidth: 0.75))
        }
    }
}

private struct RelationsView: View {
    @ObservedObject var model: AppModel
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private let groups = [("synonyms", "近义词"), ("similar", "形近词"), ("derivatives", "派生词"), ("phrases", "常用短语")]
    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 6) { ForEach(groups, id: \.0) { key, title in relationButton(key, title) } }
                LazyVGrid(columns: [GridItem(.flexible(), alignment: .leading), GridItem(.flexible(), alignment: .leading)], alignment: .leading, spacing: 6) { ForEach(groups, id: \.0) { key, title in relationButton(key, title) } }
            }
            ForEach(groups, id: \.0) { key, title in if model.expandedRelations.contains(key) {
                VStack(alignment: .leading, spacing: 9) {
                    Text(title).font(.system(size: 11, weight: .semibold))
                    let rows = model.relations[key].list
                    if rows.isEmpty {
                        Text("暂无相关词条").foregroundStyle(Palette.muted)
                    } else {
                        relatedEntries(rows)
                    }
                }.padding(13).frame(maxWidth: .infinity, alignment: .leading)
                    .background(Palette.wash, in: RoundedRectangle(cornerRadius: 9))
                    .transition(reduceMotion ? .identity : .offset(y: -6).combined(with: .opacity))
            } }
        }
    }
    private func relatedEntries(_ rows: [J]) -> some View {
        let hasLabels = rows.contains { $0["senses"].list.contains { !$0["partOfSpeech"].text.isEmpty } }
        return Grid(alignment: .topLeading, horizontalSpacing: 15, verticalSpacing: 7) {
            ForEach(Array(rows.enumerated()), id: \.offset) { index, related in
                if index > 0 { Divider().overlay(Palette.border).gridCellUnsizedAxes(.horizontal) }
                GridRow(alignment: .top) {
                    Button { model.lookup(related.wordText) } label: {
                        Text(related.wordText).multilineTextAlignment(.leading)
                    }
                    .buttonStyle(.link).font(.system(size: 14, weight: .semibold))
                    .frame(maxWidth: 154, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
                    VStack(alignment: .leading, spacing: 4) {
                        ForEach(Array(related["senses"].list.enumerated()), id: \.offset) { _, sense in
                            HStack(alignment: .firstTextBaseline, spacing: 8) {
                                if hasLabels {
                                    Text(sense["partOfSpeech"].text).font(.system(size: 12))
                                        .foregroundStyle(Palette.muted).frame(width: 66, alignment: .leading)
                                        .fixedSize(horizontal: false, vertical: true)
                                }
                                Text(sense["meaning"].text).font(.system(size: 13))
                                    .foregroundStyle(Palette.deep).frame(maxWidth: .infinity, alignment: .leading)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                        }
                    }
                }
            }
        }
    }
    private func relationButton(_ key: String, _ title: String) -> some View {
        let expanded = model.expandedRelations.contains(key)
        return Button {
            withAnimation(reduceMotion ? nil : .spring(response: 0.28, dampingFraction: 0.86)) {
                if expanded { model.expandedRelations.remove(key) }
                else { model.expandedRelations.insert(key) }
            }
        } label: {
            HStack(spacing: 6) {
                Text("\(title) · \(model.relations[key].list.count)")
                DropdownChevron(expanded: expanded)
            }
        }.buttonStyle(PillButtonStyle()).fixedSize()
    }
}
extension J { var wordText: String { self["word"].text } }

private struct ExamplesView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            ForEach(Array(model.examples.enumerated()), id: \.offset) { index, item in VStack(alignment: .leading, spacing: 7) {
                HStack(alignment: .center, spacing: 10) {
                    Text(item["source"].text)
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 9).padding(.vertical, 3)
                        .background(Palette.wash, in: Capsule())
                        .overlay(Capsule().strokeBorder(Palette.accent.opacity(0.24), lineWidth: 0.75))
                    Spacer(minLength: 0)
                    let eng = item["english"].text
                    let saved = model.isContextSentenceSaved(word: model.word, sentence: eng)
                    Button(saved ? "✓ 已收藏" : "收藏例句") {
                        if saved {
                            model.openContextReview(focusWord: model.word)
                        } else {
                            let src = item["source"].text.trimmed.isEmpty ? "真题例句" : item["source"].text
                            Task {
                                await model.saveContextSentence(
                                    word: model.word,
                                    sentence: eng,
                                    translation: item["translation"].text,
                                    source: src,
                                    matchedForm: item["matchedWords"].list.first?.text
                                )
                            }
                        }
                    }.buttonStyle(PillButtonStyle())
                        .help("收藏到「我的例句」，方便随时自测")
                    let loading = model.loadingExampleIndex == index
                    Button { model.speakExample(at: index) } label: {
                        HStack(spacing: 5) {
                            Group {
                                if loading { ProgressView().controlSize(.mini) }
                                else { Image(systemName: "speaker.wave.2") }
                            }.frame(width: 14, height: 14)
                            Text("朗读")
                        }
                    }.buttonStyle(PillButtonStyle())
                        .disabled(loading)
                        .help(loading ? "正在加载例句语音" : "朗读整条例句")
                        .accessibilityLabel(loading ? "正在加载例句语音" : "朗读整条例句")
                }
                LinkedText(text: item["english"].text, highlights: item["matchedWords"].list.map(\.text), size: 14.5, color: Palette.ink)
                if !item["translation"].text.isEmpty { LinkedText(text: item["translation"].text, chineseHighlights: item["translationTerms"].list.map(\.text), size: 13, color: Palette.muted) }
                if !item["contextEnglish"].text.isEmpty { DisclosureGroup("查看题目") { LinkedText(text: item["contextEnglish"].text, size: 13); LinkedText(text: item["contextTranslation"].text, size: 12, color: Palette.muted) }.font(.system(size: 11)) }
            }.padding(.horizontal, 14).padding(.vertical, 11).frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.chrome, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.85)) }
            if model.examplesLoading { ProgressView().controlSize(.small).frame(maxWidth: .infinity) }
            else if !model.examplesError.isEmpty { Text(model.examplesError).font(.system(size: 12)).foregroundStyle(Palette.muted); Button("重试") { Task { await model.loadExamples(reset: model.examples.isEmpty) } }.buttonStyle(MemoButtonStyle()) }
            else if model.exampleOffset < model.exampleTotal { Button("加载更多例句") { Task { await model.loadExamples(reset: false) } }.buttonStyle(MemoButtonStyle()).frame(maxWidth: .infinity) }
            else if model.examples.isEmpty { Text(model.word.isEmpty ? "查词后可查看真题例句" : "暂无例句。可切换考试类型。").font(.system(size: 11)).foregroundStyle(Palette.muted) }
        }
    }
}

private struct ChinesePage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("「\(model.chineseQuery)」的英文表达").font(.system(size: 25, weight: .semibold))
            Text(model.chineseHint).font(.system(size: 12)).foregroundStyle(Palette.muted)
            ScrollView {
                LazyVStack(spacing: 5) {
                    ForEach(model.chineseResults, id: \.wordText) { row in
                        let kind = row["kind"].text
                        let isExact = kind == "完全匹配"
                        Button { model.lookup(row["word"].text) } label: {
                            HStack(alignment: .top, spacing: 14) {
                                VStack(alignment: .leading, spacing: 5) {
                                    Text(row["word"].text).font(.system(size: 15, weight: .bold))
                                        .foregroundStyle(Palette.deep)
                                    if !kind.isEmpty {
                                        Text(kind)
                                            .font(.system(size: 10.5, weight: .semibold))
                                            .foregroundStyle(isExact ? Palette.deep : Palette.muted)
                                            .padding(.horizontal, 7).padding(.vertical, 2)
                                            .background(isExact ? Palette.wash : Palette.canvas, in: Capsule())
                                            .overlay(Capsule().strokeBorder(isExact ? Palette.accent.opacity(0.28) : Palette.border, lineWidth: 0.75))
                                    }
                                }.frame(width: 146, alignment: .leading)
                                Text(row["displayMeaning"].text).font(.system(size: 12.5))
                                    .foregroundStyle(Palette.ink)
                                    .lineLimit(3).frame(maxWidth: .infinity, alignment: .leading)
                            }
                            .padding(.horizontal, 15).padding(.vertical, 11)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Palette.paper, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.75))
                            .contentShape(Rectangle())
                        }.buttonStyle(.plain)
                    }
                    if model.chineseResults.count < model.chineseTotal {
                        Button("显示更多结果") { Task { await model.moreChinese() } }.buttonStyle(MemoButtonStyle())
                    }
                }
            }
        }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

private struct TranslationPage: View {
    @ObservedObject var model: AppModel
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var selectedWords: Set<String> = []
    @State private var batchBusy = false
    @State private var batchStatus = ""

    private var extractedKey: String {
        model.extractedWords.map { $0["word"].text }.joined(separator: ",")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 9) {
                Text("段落翻译").font(.system(size: 23, weight: .semibold))
                Spacer()
                Button(model.contextSentences.isEmpty ? "例句自测" : "例句自测 (\(model.contextSentences.count))") {
                    model.openContextReview()
                }
                .buttonStyle(PillButtonStyle())
                .help("用收藏的例句进行卡片自测")
                MemoPicker(title: "翻译方向", selection: $model.translationDirection,
                           options: [("en|zh-CN", "英译中"), ("zh-CN|en", "中译英")]).frame(width: 104)
                Button(model.translating ? "翻译中…" : "翻译") {
                    model.lastTranslationSourceLabel = "段落翻译"
                    Task { await model.translate() }
                }
                    .buttonStyle(MemoButtonStyle(accent: true)).disabled(model.translating)
            }
            TextEditor(text: $model.translationInput).font(.system(size: 15)).padding(8).frame(height: 140).background(Palette.paper, in: RoundedRectangle(cornerRadius: 10)).overlay(RoundedRectangle(cornerRadius: 10).stroke(Palette.border)).tourTarget("translation")
            Text("点击英文单词查看释义。").font(.system(size: 11)).foregroundStyle(Palette.muted)
            ScrollView {
                VStack(spacing: 14) {
                    Card {
                        VStack(alignment: .leading, spacing: 12) {
                            Text("原文").sectionLabel()
                            LinkedText(text: model.translationSource.isEmpty ? "在上方输入或粘贴原文。" : model.translationSource, size: 16)
                                .environment(\.openURL, OpenURLAction { url in
                                    if url.scheme == "memolex", let query = url.path.removingPercentEncoding {
                                        model.lookupFromTranslation(String(query.dropFirst()))
                                        return .handled
                                    }
                                    return .systemAction
                                })
                        }
                    }
                    VStack(alignment: .leading, spacing: 12) {
                        Text("译文").sectionLabel()
                        if !model.translationOutput.isEmpty {
                            LinkedText(text: model.translationOutput, size: 16).transition(.opacity)
                                .environment(\.openURL, OpenURLAction { url in
                                    if url.scheme == "memolex", let query = url.path.removingPercentEncoding {
                                        model.lookupFromTranslation(String(query.dropFirst()))
                                        return .handled
                                    }
                                    return .systemAction
                                })
                                .help(model.translationProvider.isEmpty ? "" : "翻译来源 " + model.translationProvider)
                        }
                    }
                    .padding(20)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(
                        LinearGradient(colors: [Palette.paper, Palette.wash.opacity(0.65)], startPoint: .topLeading, endPoint: .bottomTrailing),
                        in: RoundedRectangle(cornerRadius: 15, style: .continuous)
                    )
                    .overlay(RoundedRectangle(cornerRadius: 15, style: .continuous).strokeBorder(Palette.accent.opacity(0.32), lineWidth: 0.85))
                    .shadow(color: Color(red: 0.06, green: 0.24, blue: 0.36).opacity(0.06), radius: 14, y: 4)
                    .animation(reduceMotion ? nil : .easeOut(duration: 0.17), value: model.translationOutput.isEmpty)

                    if !model.extractedWords.isEmpty {
                        Card {
                            VStack(alignment: .leading, spacing: 11) {
                                HStack(spacing: 8) {
                                    Text("重点生词 · 共 \(model.extractedWords.count) 个（已选 \(selectedWords.count)）")
                                        .font(.system(size: 13.5, weight: .semibold))
                                    Spacer()
                                    Button(selectedWords.count == model.extractedWords.count ? "取消全选" : "全选") {
                                        if selectedWords.count == model.extractedWords.count {
                                            selectedWords.removeAll()
                                        } else {
                                            selectedWords = Set(model.extractedWords.map { $0["word"].text })
                                        }
                                    }.buttonStyle(MemoButtonStyle(compact: true))
                                    Button(batchBusy ? "加入中…" : "＋ 批量加入云词本") {
                                        Task { await batchAdd() }
                                    }
                                    .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                                    .disabled(batchBusy || selectedWords.isEmpty)
                                }
                                MorphemeFlowLayout(spacing: 7, lineSpacing: 7, expandLast: false) {
                                    ForEach(Array(model.extractedWords.enumerated()), id: \.offset) { _, item in
                                        let w = item["word"].text
                                        let tag = item["tags"].list.first?.text ?? ""
                                        let ipa = item["phonetic"].text
                                        let meaning = item["meaning"].text
                                        let chosen = selectedWords.contains(w)
                                        Button {
                                            if chosen { selectedWords.remove(w) }
                                            else { selectedWords.insert(w) }
                                        } label: {
                                            HStack(spacing: 5) {
                                                Text(chosen ? "✓" : "＋")
                                                    .font(.system(size: 11, weight: .bold))
                                                    .foregroundStyle(chosen ? Palette.deep : Palette.muted)
                                                Text(w)
                                                    .font(.system(size: 12.5, weight: .semibold))
                                                    .foregroundStyle(chosen ? Palette.ink : Palette.muted)
                                                if !tag.isEmpty {
                                                    Text(tag)
                                                        .font(.system(size: 10, weight: .semibold))
                                                        .foregroundStyle(Palette.deep)
                                                }
                                            }
                                            .padding(.horizontal, 10).padding(.vertical, 5)
                                            .background(chosen ? Palette.wash : Palette.paper, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                                            .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(chosen ? Palette.accent : Palette.border, lineWidth: 1))
                                        }
                                        .buttonStyle(.plain)
                                        .noFocusRing()
                                        .help("\(w)\(ipa.isEmpty ? "" : " /\(ipa)/")\n\(meaning)")
                                        .contextMenu {
                                            Button("查看「\(w)」释义") { model.lookupFromTranslation(w) }
                                            Button("收藏所在例句") {
                                                Task {
                                                    await model.saveFromTranslation(word: w)
                                                    batchStatus = "✓ 已收藏「\(w)」的例句，可点击右上角「例句自测」复习。"
                                                }
                                            }
                                        }
                                    }
                                }
                                if !batchStatus.isEmpty {
                                    Text(batchStatus).font(.system(size: 11.5)).foregroundStyle(Palette.muted)
                                }
                            }
                        }
                    }
                }
            }
        }
        .onChange(of: extractedKey) { _ in
            selectedWords = Set(model.extractedWords.map { $0["word"].text })
            batchStatus = ""
        }
    }

    private func batchAdd() async {
        let words = model.extractedWords.map { $0["word"].text }.filter { selectedWords.contains($0) }
        guard !words.isEmpty else { return }
        batchBusy = true
        batchStatus = "正在批量加入云词本…"
        defer { batchBusy = false }
        do {
            let added = try await model.batchAddToNotepad(words)
            if added == 0 {
                batchStatus = "所选 \(words.count) 个单词均已在当前云词本中。"
            } else {
                batchStatus = "✓ 已将 \(added) 个新词批量加入「\(model.selectedNotepadTitle)」"
            }
        } catch {
            batchStatus = error.localizedDescription
        }
    }
}
