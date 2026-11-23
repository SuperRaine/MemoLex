import SwiftUI

struct TourTargets: PreferenceKey {
    static var defaultValue: [String: Anchor<CGRect>] = [:]
    static func reduce(value: inout [String: Anchor<CGRect>], nextValue: () -> [String: Anchor<CGRect>]) { value.merge(nextValue()) { _, new in new } }
}
extension View {
    func tourTarget(_ name: String) -> some View { anchorPreference(key: TourTargets.self, value: .bounds) { [name: $0] } }
}
struct TourStep {
    var title: String
    var text: String
    var target: String
    var settings: String? = nil
}
@MainActor extension AppModel {
    static let tourSteps: [TourStep] = [
        .init(title: "查词与阅读", text: "输入英文或中文，按回车查词。点击英文单词可查看释义，点击「英音」或「美音」听发音。", target: "search"),
        .init(title: "登录与切换账号", text: "在这里登录墨墨。选择已保存的账号即可切换。", target: "login", settings: "墨墨账号"),
        .init(title: "顽固单词", text: "点击顶部单词查词。在“全部”中选择排序，或遮住释义自测。", target: "stubborn"),
        .init(title: "收藏与管理云词本", text: "点击“加入云词本”收藏，进入“查看 / 编辑”管理单词和查看释义。", target: "cloud"),
        .init(title: "今日新学", text: "将单词加入今日新学，添加时请留意手机端同步提醒。", target: "today"),
        .init(title: "段落翻译", text: "输入一段文字后点击翻译，英文单词也可点击查词。", target: "translation"),
        .init(title: "截图翻译", text: "框选屏幕中的英文。单个词直接查词，段落进入翻译。", target: "screenshot"),
        .init(title: "鼠标取词", text: "开启后，将鼠标停在英文单词上即可查看释义。", target: "hover", settings: "鼠标取词"),
        .init(title: "剪贴板查词", text: "开启后，选中文字连按两次 ⌘C，唤起软件查词或翻译。", target: "clipboard", settings: "常规"),
        .init(title: "考试倒计时", text: "设置考试名称，分别选择或输入年、月、日。开启后可在标题栏看到剩余天数。", target: "countdown", settings: "常规"),
        .init(title: "软件更新", text: "在这里检查新版。下载完成后，点击“更新并重启”安装。", target: "updates", settings: "软件更新"),
        .init(title: "系统权限", text: "在这里查看取词和截图权限。更新后若无法授权，可重置对应的旧授权。", target: "permissions", settings: "系统权限")
    ]
    func startTour() { showWindow?(); setTourStep(0) }
    func setTourStep(_ index: Int) {
        guard Self.tourSteps.indices.contains(index) else { finishTour(); return }
        tourIndex = index; let step = Self.tourSteps[index]
        if let page = step.settings { settingsPage = page; sheet = .settings }
        else { sheet = nil; page = step.target == "translation" ? .translation : .word }
    }
    func finishTour() { tourIndex = nil; sheet = nil; preferences.onboardingCompleted = true; preferences.save() }
}

struct TourOverlay: View {
    @ObservedObject var model: AppModel
    var anchors: [String: Anchor<CGRect>]
    var context: String
    var body: some View {
        if let index = model.tourIndex, AppModel.tourSteps.indices.contains(index) {
            let step = AppModel.tourSteps[index]
            if (step.settings == nil ? "main" : "settings") == context {
                GeometryReader { geometry in
                    let target = anchors[step.target].map { geometry[$0].insetBy(dx: -5, dy: -5) } ?? CGRect(x: 15, y: 15, width: 150, height: 40)
                    ZStack {
                        Path { path in path.addRect(CGRect(origin: .zero, size: geometry.size)); path.addRoundedRect(in: target, cornerSize: CGSize(width: 8, height: 8)) }.fill(.black.opacity(0.43), style: FillStyle(eoFill: true)).allowsHitTesting(false)
                        RoundedRectangle(cornerRadius: 8).stroke(Palette.accent, lineWidth: 2.5).frame(width: target.width, height: target.height).position(x: target.midX, y: target.midY).allowsHitTesting(false)
                        VStack(alignment: .leading, spacing: 14) {
                            HStack {
                                Text("\(index + 1) / \(AppModel.tourSteps.count)").font(.system(size: 11)).foregroundStyle(Palette.deep)
                                MemoPicker(title: "引导步骤", selection: Binding(get: { index }, set: { model.setTourStep($0) }),
                                           options: AppModel.tourSteps.enumerated().map { ($0.offset, $0.element.title) }, height: 29)
                            }
                            Text(step.text).font(.system(size: 13)).lineSpacing(5).foregroundStyle(Palette.deep)
                            HStack { Button("跳过") { model.finishTour() }; Spacer(); Button("上一步") { model.setTourStep(index - 1) }.disabled(index == 0); Button(index == AppModel.tourSteps.count - 1 ? "完成" : "下一步") { model.setTourStep(index + 1) }.buttonStyle(MemoButtonStyle(accent: true, compact: true)) }.buttonStyle(MemoButtonStyle(compact: true))
                        }.padding(17).frame(width: min(340, geometry.size.width - 32)).background(Palette.paper, in: RoundedRectangle(cornerRadius: 12)).overlay(RoundedRectangle(cornerRadius: 12).stroke(Palette.accent)).shadow(color: .black.opacity(0.2), radius: 15, y: 4).position(cardPosition(target: target, size: geometry.size))
                    }
                }.transition(.opacity)
            }
        }
    }
    private func cardPosition(target: CGRect, size: CGSize) -> CGPoint {
        let width: CGFloat = min(340, size.width - 32); let height: CGFloat = 200
        let x = target.maxX + width + 25 < size.width ? target.maxX + width / 2 + 15 : size.width - width / 2 - 16
        var y = max(height / 2 + 16, min(size.height - height / 2 - 16, target.minY + height / 2))
        if x - width / 2 < target.maxX, x + width / 2 > target.minX, target.maxY + height + 25 < size.height { y = target.maxY + height / 2 + 15 }
        return CGPoint(x: x, y: y)
    }
}
