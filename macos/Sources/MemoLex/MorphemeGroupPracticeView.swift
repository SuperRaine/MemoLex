import SwiftUI

struct MorphemeGroupPracticeView: View {
    @ObservedObject var model: AppModel
    let group: J
    @State private var questions: [J] = []
    @State private var currentIndex = 0
    @State private var firstTryCorrect = 0
    @State private var answeredCorrectly = false
    @State private var missedCurrent = false
    @State private var wrongPicked: Set<String> = []
    @State private var showAffixHint = false
    @State private var feedback = ""
    @State private var feedbackCorrect = false
    @State private var finished = false

    private var rootForm: String { group["rootForm"].text }
    private var rootMeaning: String { group["rootMeaning"].text }
    private var members: [J] { group["members"].list }
    private var currentQuestion: J? {
        guard !finished, questions.indices.contains(currentIndex) else { return nil }
        return questions[currentIndex]
    }

    var body: some View {
        let cols = Array(repeating: GridItem(.flexible(), spacing: 10), count: members.count <= 4 ? 2 : 3)
        VStack(spacing: 0) {
            DialogHeader(title: "词根「\(rootForm)」同组辨析") { model.sheet = nil }
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .center, spacing: 10) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text("词根 \(rootForm)（\(rootMeaning)）· 同根辨析")
                            .font(.system(size: 19, weight: .bold))
                        Text("共 \(members.count) 个同源词并排对比，优先挑选你查过或收藏过的词。")
                            .font(.system(size: 11.5))
                            .foregroundStyle(Palette.muted)
                    }
                    Spacer()
                    let doneCount = finished ? questions.count : (currentIndex + (answeredCorrectly ? 1 : 0))
                    Text("一次答对 \(firstTryCorrect) / \(doneCount)")
                        .font(.system(size: 11.5, weight: .semibold))
                        .foregroundStyle(Palette.deep)
                        .padding(.horizontal, 10).padding(.vertical, 4)
                        .background(Palette.wash, in: Capsule())
                        .overlay(Capsule().strokeBorder(Palette.accent.opacity(0.28), lineWidth: 0.8))
                    Button("重新出题") { startQuiz() }
                        .buttonStyle(MemoButtonStyle(compact: true))
                }

                // Question card
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text(finished
                             ? "本组词根辨析完成"
                             : "第 \(currentIndex + 1) / \(max(1, questions.count)) 题 · 选择对应的英文单词")
                            .font(.system(size: 11.5, weight: .semibold))
                            .foregroundStyle(Palette.deep)
                        Spacer()
                        if !finished && !answeredCorrectly {
                            Button("词缀提示") { showAffixHint = true }
                                .buttonStyle(PillButtonStyle())
                                .help("查看本题正确答案的词缀线索")
                        }
                    }

                    if finished {
                        Text(firstTryCorrect == questions.count
                             ? "全部掌握！「\(rootForm)」这组 \(members.count) 个词一次答对。"
                             : "完成本轮练习（一次答对 \(firstTryCorrect) / \(questions.count)），下方可对比复习全部词缀差异。")
                            .font(.system(size: 17, weight: .bold))
                            .fixedSize(horizontal: false, vertical: true)
                    } else if let current = currentQuestion {
                        Text("哪个词表示「\(current["promptMeaning"].text)」？")
                            .font(.system(size: 18, weight: .bold))
                            .fixedSize(horizontal: false, vertical: true)
                        if showAffixHint {
                            Text(affixHintText(for: current))
                                .font(.system(size: 12))
                                .foregroundStyle(Palette.deep)
                        }
                    }

                    if !feedback.isEmpty {
                        Text(feedback)
                            .font(.system(size: 12.5, weight: .semibold))
                            .foregroundStyle(feedbackCorrect
                                             ? Color(red: 0.06, green: 0.54, blue: 0.36)
                                             : Color(red: 0.78, green: 0.16, blue: 0.26))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                .padding(16)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.wash.opacity(0.65), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Palette.accent.opacity(0.28), lineWidth: 0.85))

                ScrollView {
                    LazyVGrid(columns: cols, spacing: 10) {
                        ForEach(Array(members.enumerated()), id: \.offset) { index, member in
                            memberCard(member: member, index: index, revealAll: finished || answeredCorrectly)
                        }
                    }
                    .padding(.vertical, 2)
                }

                HStack {
                    Text(finished
                         ? "点击任意卡片上的「查词」可跳转主界面查看完整例句与助记。"
                         : answeredCorrectly
                            ? "按空格或回车进入下一题，也可点卡片上的「查词」深入查看"
                            : "快捷键：数字键 1–6 选择单词 · 对比词缀前缀含义来判断")
                        .font(.system(size: 11))
                        .foregroundStyle(Palette.muted)
                    Spacer()
                    if answeredCorrectly || finished {
                        Button(finished ? "再练一遍" : (currentIndex + 1 < questions.count ? "下一题 →" : "查看总结")) {
                            advance()
                        }
                        .buttonStyle(MemoButtonStyle(accent: true))
                        .keyboardShortcut(.return, modifiers: [])
                    }
                }
            }
            .padding(22)
            .overlay {
                Group {
                    if !finished && !answeredCorrectly {
                        ForEach(Array(members.prefix(6).enumerated()), id: \.offset) { idx, member in
                            Button("") { selectChoice(member) }
                                .keyboardShortcut(KeyEquivalent(Character("\(idx + 1)")), modifiers: [])
                        }
                    } else {
                        Button("") { advance() }
                            .keyboardShortcut(.space, modifiers: [])
                    }
                }
                .opacity(0)
                .frame(width: 0, height: 0)
                .allowsHitTesting(false)
            }
        }
        .frame(width: 740, height: min(650, (NSScreen.main?.visibleFrame.height ?? 760) - 60))
        .background(Palette.paper)
        .foregroundStyle(Palette.ink)
        .font(.system(size: 13))
        .noFocusRing()
        .onAppear { startQuiz() }
    }

    private func memberCard(member: J, index: Int, revealAll: Bool) -> some View {
        let word = member["word"].text
        let meaning = member["meaning"].text
        let isUserWord = member["isUserWord"].flag
        let isAnswer = currentQuestion?["word"].text.caseInsensitiveCompare(word) == .orderedSame
        let isWrong = wrongPicked.contains(word.lowercased())
        let showDetail = revealAll || isWrong

        let bgColor: Color = {
            if revealAll && isAnswer { return Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.14) }
            if isWrong { return Color(red: 0.96, green: 0.25, blue: 0.37).opacity(0.11) }
            return Palette.paper
        }()
        let borderColor: Color = {
            if revealAll && isAnswer { return Color(red: 0.08, green: 0.72, blue: 0.48).opacity(0.65) }
            if isWrong { return Color(red: 0.96, green: 0.25, blue: 0.37).opacity(0.55) }
            return Palette.border
        }()

        return VStack(alignment: .leading, spacing: 7) {
            HStack(spacing: 6) {
                if !finished {
                    Text("\(index + 1)")
                        .font(.system(size: 10.5, weight: .bold))
                        .foregroundStyle(Palette.muted)
                        .frame(width: 20, height: 20)
                        .background(Palette.wash, in: Circle())
                }
                Text(word)
                    .font(.system(size: 16.5, weight: .bold))
                    .foregroundStyle(Palette.ink)
                    .lineLimit(1)
                Spacer(minLength: 4)
                if isUserWord {
                    Text("查过/收藏")
                        .font(.system(size: 9.5, weight: .semibold))
                        .foregroundStyle(Color(red: 0.65, green: 0.34, blue: 0.03))
                        .padding(.horizontal, 5).padding(.vertical, 2)
                        .background(Color(red: 0.96, green: 0.62, blue: 0.04).opacity(0.16), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
                }
                if showDetail {
                    Button("查词") {
                        model.sheet = nil
                        model.lookup(word)
                    }
                    .buttonStyle(PillButtonStyle())
                    .help("在主界面查看「\(word)」")
                }
            }

            let segments = member["segments"].list
            MorphemeFlowLayout(spacing: 4, lineSpacing: 4, expandLast: false) {
                ForEach(Array(segments.enumerated()), id: \.offset) { segIdx, seg in
                    if segIdx > 0 {
                        Text("+")
                            .font(.system(size: 10.5, weight: .bold))
                            .foregroundStyle(Palette.accent.opacity(0.8))
                    }
                    let isRoot = seg["isTargetRoot"].flag
                    let segMeaning = seg["meaning"].text
                    VStack(alignment: .leading, spacing: 1) {
                        Text(seg["form"].text)
                            .font(.system(size: 13, weight: .bold))
                            .foregroundStyle(isRoot ? Palette.muted : Palette.deep)
                        if showDetail && !segMeaning.isEmpty {
                            Text(segMeaning)
                                .font(.system(size: 9.5))
                                .foregroundStyle(Palette.muted)
                        }
                    }
                    .padding(.horizontal, 6).padding(.vertical, 2)
                    .background(isRoot ? Palette.canvas : Palette.wash, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(isRoot ? Palette.border : Palette.accent.opacity(0.32), lineWidth: 0.8))
                }
            }

            if showDetail {
                Text(meaning)
                    .font(.system(size: 12))
                    .foregroundStyle(isAnswer && revealAll ? Palette.ink : Palette.muted)
                    .lineSpacing(2)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text("点击选择此词")
                    .font(.system(size: 11))
                    .foregroundStyle(Palette.muted.opacity(0.75))
            }
            Spacer(minLength: 0)
        }
        .padding(12)
        .frame(maxWidth: .infinity, minHeight: 110, alignment: .topLeading)
        .background(bgColor, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(borderColor, lineWidth: (revealAll && isAnswer) ? 1.5 : 0.9))
        .contentShape(RoundedRectangle(cornerRadius: 11, style: .continuous))
        .onTapGesture {
            if !finished && !answeredCorrectly { selectChoice(member) }
        }
    }

    private func affixHintText(for member: J) -> String {
        let affixes = member["segments"].list.filter { !$0["isTargetRoot"].flag }
        if affixes.isEmpty {
            return "构词线索：\(member["explanation"].text)"
        }
        let pieces = affixes.map { seg -> String in
            let form = seg["form"].text
            let mean = seg["meaning"].text
            return mean.isEmpty ? form : "\(form)（\(mean)）"
        }
        return "词缀线索：" + pieces.joined(separator: " ＋ ")
    }

    private func startQuiz() {
        questions = members.shuffled()
        currentIndex = 0
        firstTryCorrect = 0
        answeredCorrectly = false
        missedCurrent = false
        wrongPicked = []
        showAffixHint = false
        feedback = ""
        feedbackCorrect = false
        finished = false
    }

    private func selectChoice(_ picked: J) {
        guard !finished, !answeredCorrectly, let current = currentQuestion else { return }
        let pickedWord = picked["word"].text
        let currentWord = current["word"].text
        if pickedWord.caseInsensitiveCompare(currentWord) == .orderedSame {
            answeredCorrectly = true
            if !missedCurrent {
                firstTryCorrect += 1
            }
            feedback = "✓ 正确！\(currentWord)：\(current["explanation"].text)"
            feedbackCorrect = true
            model.speakWord(currentWord, accent: .american)
        } else {
            missedCurrent = true
            wrongPicked.insert(pickedWord.lowercased())
            feedback = "✗ \(pickedWord) 表示「\(picked["promptMeaning"].text)」，再看看其他词缀！"
            feedbackCorrect = false
        }
    }

    private func advance() {
        if finished {
            startQuiz()
            return
        }
        guard answeredCorrectly else { return }
        currentIndex += 1
        answeredCorrectly = false
        missedCurrent = false
        wrongPicked = []
        showAffixHint = false
        feedback = ""
        if currentIndex >= questions.count { finished = true }
    }
}
