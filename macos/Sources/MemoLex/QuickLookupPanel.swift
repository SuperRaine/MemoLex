import AppKit
import SwiftUI

private final class SpotlightPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

@MainActor final class QuickLookupController: NSObject, NSWindowDelegate {
    private unowned let model: AppModel
    private var panel: NSPanel?
    private let state = QuickLookupState()
    private var globalMouseMonitor: Any?
    private var localMouseMonitor: Any?

    init(model: AppModel) {
        self.model = model
        super.init()
    }

    func toggle() {
        if let panel, panel.isVisible {
            hide()
        } else {
            show()
        }
    }

    func show() {
        if panel == nil {
            let p = SpotlightPanel(
                contentRect: NSRect(x: 0, y: 0, width: 610, height: 320),
                styleMask: [.borderless, .nonactivatingPanel],
                backing: .buffered,
                defer: false
            )
            p.isOpaque = false
            p.backgroundColor = .clear
            p.hasShadow = true
            p.level = .floating
            p.isReleasedWhenClosed = false
            p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
            p.delegate = self
            let view = QuickLookupView(
                model: model,
                state: state,
                dismiss: { [weak self] in self?.hide() },
                openMain: { [weak self] word in
                    self?.hide()
                    self?.model.lookup(word)
                }
            )
            p.contentView = NSHostingView(rootView: view)
            panel = p
        }
        guard let panel else { return }
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1280, height: 800)
        let x = screen.midX - panel.frame.width / 2
        let y = screen.maxY - min(180, screen.height * 0.22) - panel.frame.height
        panel.setFrameOrigin(NSPoint(x: x, y: max(screen.minY + 40, y)))
        state.focusNonce += 1
        state.status = ""
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        installOutsideClickMonitors()
    }

    func hide() {
        removeOutsideClickMonitors()
        panel?.orderOut(nil)
    }

    private func installOutsideClickMonitors() {
        removeOutsideClickMonitors()
        globalMouseMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown]) { [weak self] _ in
            Task { @MainActor in self?.dismissIfClickOutside() }
        }
        localMouseMonitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown]) { [weak self] event in
            self?.dismissIfClickOutside()
            return event
        }
    }

    private func removeOutsideClickMonitors() {
        if let globalMouseMonitor { NSEvent.removeMonitor(globalMouseMonitor); self.globalMouseMonitor = nil }
        if let localMouseMonitor { NSEvent.removeMonitor(localMouseMonitor); self.localMouseMonitor = nil }
    }

    private func dismissIfClickOutside() {
        guard !state.suppressDismiss, let panel, panel.isVisible else { return }
        if !panel.frame.contains(NSEvent.mouseLocation) {
            hide()
        }
    }

    func windowDidResignKey(_ notification: Notification) {
        if !state.suppressDismiss {
            hide()
        }
    }
}

@MainActor final class QuickLookupState: ObservableObject {
    @Published var query = ""
    @Published var result: J = .null
    @Published var status = ""
    @Published var focusNonce = 0
    var suppressDismiss = false
}

private struct QuickLookupView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var state: QuickLookupState
    let dismiss: () -> Void
    let openMain: (String) -> Void
    @FocusState private var focused: Bool
    @State private var searchTask: Task<Void, Never>?

    private var entry: J { state.result["entry"] }
    private var resolvedWord: String {
        let w = state.result["word"].text
        return w.isEmpty ? state.query.trimmed : w
    }
    private var spellingSuggestions: [String] {
        state.result["spellingSuggestions"].list.map(\.text)
    }
    private var prefixSuggestions: [String] {
        state.result["suggestions"].list.map(\.text).filter { $0.caseInsensitiveCompare(resolvedWord) != .orderedSame }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 11) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(Palette.deep)
                TextField("快速查词… 输入英文或中文，按 ↩ 展开", text: $state.query)
                    .textFieldStyle(.plain)
                    .font(.system(size: 18))
                    .focused($focused)
                    .onSubmit {
                        let target = resolvedWord
                        if !target.isEmpty { openMain(target) }
                    }
                    .onChange(of: state.query) { value in runLookup(value) }
                Text("Esc")
                    .font(.system(size: 10.5, weight: .semibold))
                    .foregroundStyle(Palette.muted)
                    .padding(.horizontal, 7).padding(.vertical, 3)
                    .background(Palette.wash, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
            }
            .padding(.horizontal, 18).padding(.vertical, 14)

            if !state.query.trimmed.isEmpty {
                Divider().overlay(Palette.border)
                VStack(alignment: .leading, spacing: 10) {
                    if entry != .null {
                        HStack(alignment: .firstTextBaseline, spacing: 8) {
                            Text(entry["word"].text)
                                .font(.system(size: 23, weight: .bold))
                                .foregroundStyle(Palette.ink)
                            let ipa = entry["phonetic"].text
                            if !ipa.isEmpty {
                                Text("/\(ipa)/")
                                    .font(.system(size: 13))
                                    .foregroundStyle(Palette.muted)
                            }
                            Spacer(minLength: 6)
                            Button { model.speakWord(entry["word"].text, accent: .american) } label: {
                                Label("朗读", systemImage: "speaker.wave.2")
                            }.buttonStyle(PillButtonStyle())
                            Button("＋ 收藏") { bookmarkCurrent(entry["word"].text) }
                                .buttonStyle(MemoButtonStyle(compact: true))
                            Button("展开主窗口 →") { openMain(entry["word"].text) }
                                .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                        }

                        Text(entry["translation"].text.decodedLines)
                            .font(.system(size: 14.5))
                            .lineSpacing(3)
                            .lineLimit(4)
                            .foregroundStyle(Palette.ink)

                        if let note = state.result["notes"].list.first?["text"].text, !note.isEmpty {
                            HStack(alignment: .top, spacing: 8) {
                                Text("记")
                                    .font(.system(size: 10.5, weight: .bold))
                                    .foregroundStyle(Palette.deep)
                                    .frame(width: 20, height: 20)
                                    .background(Palette.accent.opacity(0.2), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
                                Text(note)
                                    .font(.system(size: 12.5))
                                    .lineLimit(2)
                                    .foregroundStyle(Palette.deep)
                            }
                            .padding(.horizontal, 11).padding(.vertical, 8)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Palette.wash, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                            .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Palette.border, lineWidth: 0.8))
                        }

                        if !prefixSuggestions.isEmpty {
                            suggestionPills(Array(prefixSuggestions.prefix(5)))
                        }
                    } else if !spellingSuggestions.isEmpty {
                        Text("未找到精确匹配，你是不是要查：")
                            .font(.system(size: 12.5, weight: .semibold))
                            .foregroundStyle(Palette.deep)
                        suggestionPills(spellingSuggestions)
                    } else {
                        HStack {
                            Text("按 Enter 在主窗口中继续查词")
                                .font(.system(size: 13))
                                .foregroundStyle(Palette.muted)
                            Spacer()
                            Button("在主窗口打开 →") { openMain(state.query.trimmed) }
                                .buttonStyle(MemoButtonStyle(accent: true, compact: true))
                        }
                    }

                    if !state.status.isEmpty {
                        Text(state.status)
                            .font(.system(size: 11.5))
                            .foregroundStyle(Palette.muted)
                    }
                }
                .padding(.horizontal, 18).padding(.vertical, 14)
            }
            Spacer(minLength: 0)
        }
        .frame(width: 610, height: 320, alignment: .topLeading)
        .background(Palette.paper, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Palette.border, lineWidth: 1.1))
        .foregroundStyle(Palette.ink)
        .noFocusRing()
        .onExitCommand { dismiss() }
        .onChange(of: state.focusNonce) { _ in focused = true }
        .onAppear { focused = true }
    }

    private func suggestionPills(_ words: [String]) -> some View {
        MorphemeFlowLayout(spacing: 6, lineSpacing: 6, expandLast: false) {
            ForEach(words, id: \.self) { candidate in
                Button(candidate) { state.query = candidate }
                    .buttonStyle(PillButtonStyle())
            }
        }
    }

    private func runLookup(_ raw: String) {
        searchTask?.cancel()
        state.status = ""
        let trimmed = raw.trimmed
        guard !trimmed.isEmpty, model.ready else { state.result = .null; return }
        searchTask = Task {
            try? await Task.sleep(nanoseconds: 90_000_000)
            guard !Task.isCancelled else { return }
            if let value = try? await model.core.request("dictionary.lookup", ["word": .string(trimmed)]),
               !Task.isCancelled, state.query.trimmed == trimmed {
                if value["entry"] == .null, let first = value["suggestions"].list.first?.text, !first.isEmpty, trimmed.count >= 2 {
                    if let fallback = try? await model.core.request("dictionary.lookup", ["word": .string(first)]),
                       !Task.isCancelled, state.query.trimmed == trimmed, fallback["entry"] != .null {
                        state.result = fallback
                        return
                    }
                }
                state.result = value
            }
        }
    }

    private func bookmarkCurrent(_ word: String) {
        guard let target = model.hoverBookmarkTarget else {
            state.status = model.loggedIn ? "请先在主窗口选择一个云词本。" : "请先登录墨墨账号。"
            return
        }
        state.suppressDismiss = true
        state.status = "正在加入「\(target.title)」…"
        Task {
            defer { state.suppressDismiss = false }
            do {
                try await model.saveHoverBookmark(word, target: target)
                state.status = "✓ 已加入「\(target.title)」"
            } catch {
                state.status = error.localizedDescription
            }
        }
    }
}
