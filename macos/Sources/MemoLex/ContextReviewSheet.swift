import SwiftUI

struct ContextReviewSheet: View {
    @ObservedObject var model: AppModel

    private var orderedItems: [J] {
        let all = model.contextSentences
        guard let focus = model.contextReviewFocusWord, !focus.isEmpty else { return all }
        let focused = all.filter { $0["word"].text.caseInsensitiveCompare(focus) == .orderedSame }
        let others = all.filter { $0["word"].text.caseInsensitiveCompare(focus) != .orderedSame }
        return focused + others
    }

    private var uniqueWords: [String] {
        var seen = Set<String>()
        var result: [String] = []
        for item in orderedItems {
            let w = item["word"].text.trimmed
            if !w.isEmpty, seen.insert(w.lowercased()).inserted {
                result.append(w)
            }
        }
        return result
    }

    private var contextMap: [String: J] {
        var map: [String: J] = [:]
        for item in orderedItems {
            let key = item["word"].text.trimmed.lowercased()
            if !key.isEmpty, map[key] == nil {
                map[key] = item
            }
        }
        return map
    }

    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "我的原句 · 离线自测") { model.sheet = nil }
            if uniqueWords.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "text.quote").font(.system(size: 34)).foregroundStyle(Palette.muted)
                    Text("还没有保存的阅读原句").font(.title3.weight(.semibold))
                    Text("在段落翻译、截图翻译或真题例句中点击「保存原句」，即可在此离线自测。")
                        .font(.callout).foregroundStyle(Palette.muted)
                    Button("知道了") { model.sheet = nil }
                        .buttonStyle(MemoButtonStyle(accent: true))
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .padding(24)
            } else {
                StubbornFlashcardView(
                    core: model.core,
                    words: uniqueWords,
                    title: "我的原句 · 离线自测",
                    savedProgress: nil,
                    persistsProgress: false,
                    preferences: model.preferences,
                    onSpeak: { model.speakWord($0, accent: $1) },
                    contextItemsByWord: contextMap,
                    contextMode: true,
                    onContextReviewed: { id in Task { await model.recordContextReview(id: id) } }
                ) {
                    model.sheet = nil
                }
                .padding(23)
            }
        }
        .frame(width: 720, height: min(650, (NSScreen.main?.visibleFrame.height ?? 760) - 60))
        .background(Palette.paper)
        .foregroundStyle(Palette.ink)
        .font(.system(size: 13))
        .noFocusRing()
    }
}
