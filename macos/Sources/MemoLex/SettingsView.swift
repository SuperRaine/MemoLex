import AppKit
import SwiftUI

struct SettingsView: View {
    @MainActor static var preferredSize: NSSize {
        let bounds = NSScreen.main?.visibleFrame.size ?? NSSize(width: 1100, height: 820)
        return NSSize(width: min(900, bounds.width - 65), height: min(650, bounds.height - 65))
    }
    @ObservedObject var model: AppModel
    @State private var status = ""
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private let pages = [
        ("常规", "gearshape"), ("墨墨账号", "person.crop.circle"),
        ("鼠标取词", "cursorarrow"), ("系统权限", "lock.shield"), ("快捷键", "keyboard"),
        ("软件更新", "arrow.down.circle"), ("反馈", "bubble.left"), ("关于与赞助", "info.circle")
    ]
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                ScrollView {
                    VStack(spacing: 3) {
                        ForEach(pages, id: \.0) { page, icon in
                            let selected = page == model.settingsPage
                            Button { model.settingsPage = page } label: {
                                HStack(spacing: 10) {
                                    Image(systemName: icon).font(.system(size: 14, weight: .medium))
                                        .frame(width: 21).accessibilityHidden(true)
                                    Text(page).font(.system(size: 13, weight: selected ? .semibold : .regular))
                                }.frame(maxWidth: .infinity, minHeight: 33, alignment: .leading)
                                    .contentShape(Rectangle())
                            }.buttonStyle(SidebarRowStyle(selected: selected))
                             .foregroundStyle(selected ? Palette.deep : Palette.ink)
                             .accessibilityAddTraits(selected ? .isSelected : [])
                        }
                    }.padding(10)
                }.frame(width: 174).background(Palette.sidebar)
                Palette.border.frame(width: 1)
                VStack(spacing: 0) {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 17) {
                            settingsHeading
                            pageContent.id(model.settingsPage).transition(.opacity)
                        }.padding(24).frame(maxWidth: 780, alignment: .leading)
                         .frame(maxWidth: .infinity, alignment: .leading)
                    }.frame(maxHeight: .infinity)
                     .animation(reduceMotion ? nil : .easeOut(duration: 0.17), value: model.settingsPage)
                    if !status.isEmpty {
                        Text(status.statusMessage).font(.footnote).foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading).padding([.horizontal, .bottom], 18)
                    }
                }
            }.frame(maxHeight: .infinity)
        }.frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Palette.canvas).foregroundStyle(Palette.ink).tint(Palette.accent).toggleStyle(.checkbox)
            .onChange(of: model.settingsPage) { _ in status = "" }
            .onExitCommand { model.sheet = nil }
            .overlayPreferenceValue(TourTargets.self) { anchors in TourOverlay(model: model, anchors: anchors, context: "settings") }
            .sheet(isPresented: $model.showLogin) { LoginView(model: model) }
    }
    private var settingsHeading: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 5) { Text(model.settingsPage).font(.system(size: 21, weight: .semibold)); Text(description).font(.system(size: 12)).foregroundStyle(Palette.muted) }
            Spacer()
            if model.settingsPage == "关于与赞助" {
                HStack(spacing: 6) {
                    Button("软件主页") { open(AppPaths.homepage) }
                    Button("夸克下载") { open(AppPaths.quarkDownload) }
                    Button("GitHub") { open(AppPaths.repository) }
                }.buttonStyle(MemoButtonStyle(compact: true)).fixedSize()
                    .tourTarget("download-links")
            }
        }
    }
    private var description: String {
        switch model.settingsPage {
        case "常规": return "设置关闭方式、启动选项和考试倒计时。"
        case "墨墨账号": return "登录墨墨后可使用云词本和学习记录。"
        case "鼠标取词": return "鼠标停在英文单词上时显示释义，移开后自动收起。"
        case "系统权限": return "按需要授权取词、截图和全局快捷键。"
        case "快捷键": return "点击输入框，按下想用的组合键。"
        case "软件更新": return "检查新版本，设置自动更新。"
        case "反馈": return "遇到问题或有改进建议，欢迎联系我们。"
        default: return "墨墨搜词 MemoLex · \(AppPaths.version)"
        }
    }
    @ViewBuilder private var pageContent: some View {
        switch model.settingsPage {
        case "常规": GeneralSettings(model: model, preferences: model.preferences)
        case "墨墨账号": AccountSettings(model: model)
        case "鼠标取词": HoverSettings(preferences: model.preferences)
        case "系统权限": PermissionsSettings(model: model)
        case "快捷键": ShortcutSettings(preferences: model.preferences)
        case "软件更新": UpdatesSettings(model: model)
        case "反馈": FeedbackSettings(status: $status)
        default: AboutSettings()
        }
    }
    private func open(_ value: String) { if let url = URL(string: value) { NSWorkspace.shared.open(url) } }
}

private struct GeneralSettings: View {
    @ObservedObject var model: AppModel
    @ObservedObject var preferences: Preferences
    var body: some View {
        VStack(alignment: .leading, spacing: 15) {
            SettingsCard(title: "考试倒计时") {
                Toggle("在标题栏显示考试倒计时", isOn: $preferences.showCountdown).tourTarget("countdown")
                ViewThatFits(in: .horizontal) {
                    HStack(alignment: .top, spacing: 16) { examName; examDate }.fixedSize(horizontal: true, vertical: false)
                    VStack(alignment: .leading, spacing: 12) { examName; examDate }
                }.disabled(!preferences.showCountdown)
            }
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: 12) { closeBehavior; launchBehavior }
                VStack(spacing: 12) { closeBehavior; launchBehavior }
            }
            Button("查看新手引导") { model.sheet = nil; model.startTour() }.buttonStyle(MemoButtonStyle())
        }
    }
    private var examName: some View {
        VStack(alignment: .leading, spacing: 7) {
            Text("考试名称").sectionLabel()
            TextField("考试", text: $preferences.examName).inputFrame().accessibilityLabel("考试名称")
        }.frame(width: 164)
    }
    private var examDate: some View {
        VStack(alignment: .leading, spacing: 7) {
            Text("考试日期").sectionLabel()
            ExamDateEntry(date: $preferences.examDate)
        }
    }
    private var closeBehavior: some View {
        SettingsCard(title: "关闭窗口") {
            Picker("关闭窗口", selection: $preferences.closeToTray) {
                Text("收起到菜单栏").tag(true)
                Text("退出软件").tag(false)
            }.pickerStyle(.radioGroup).labelsHidden()
            Toggle("记住关闭方式，下次不再询问", isOn: $preferences.rememberClose)
        }.frame(minWidth: 195)
    }
    private var launchBehavior: some View {
        SettingsCard(title: "启动与快捷查词") {
            Toggle("登录 Mac 时自动启动", isOn: $preferences.startAtLogin)
            Toggle("连按两次 ⌘C 查词或翻译", isOn: $preferences.doubleCopy).tourTarget("clipboard")
            Text("选中文字后，在 750 毫秒内连按两次 ⌘C。").font(.system(size: 11)).foregroundStyle(Palette.muted)
        }.frame(minWidth: 195)
    }
}

struct SettingsCard<Content: View>: View {
    var title: String
    @ViewBuilder var content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(.system(size: 14, weight: .semibold))
            content
        }.padding(15).frame(maxWidth: .infinity, alignment: .leading)
            .background(Palette.paper, in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Palette.border, lineWidth: 0.6).allowsHitTesting(false))
    }
}

private struct HoverSettings: View {
    @ObservedObject var preferences: Preferences
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            SettingsCard(title: "取词开关") {
                Grid(alignment: .leading, horizontalSpacing: 25, verticalSpacing: 10) {
                    GridRow { Toggle("开启鼠标自动取词", isOn: $preferences.hoverEnabled).tourTarget("hover"); Toggle("取词浮窗半透明", isOn: $preferences.hoverTranslucent) }
                    GridRow { Toggle("未找到释义时显示相近单词", isOn: $preferences.hoverNearest); Toggle("找到单词后自动加入云词本", isOn: $preferences.hoverAutoAdd) }
                }
            }
            SettingsCard(title: "触发与外观") {
                Grid(alignment: .leading, horizontalSpacing: 20, verticalSpacing: 14) {
                    GridRow { Text("取词方式").foregroundStyle(Palette.muted); HStack(spacing: 12) { Text("鼠标悬停 ＋").foregroundStyle(Palette.muted); modifier("⌃", flag: 1); modifier("⌥", flag: 2); modifier("⇧", flag: 4) }.frame(height: 36) }
                    GridRow { Text("取词延迟").foregroundStyle(Palette.muted); HStack { TextField("750", value: $preferences.hoverDelay, format: .number).inputFrame().frame(width: 100); Text("毫秒（100–3000）").foregroundStyle(Palette.muted) } }
                    GridRow {
                        Text("浮窗字体大小").foregroundStyle(Palette.muted)
                        MemoPicker(title: "浮窗字体大小", selection: $preferences.hoverFontSize,
                                   options: [("small", "较小"), ("standard", "标准"), ("large", "较大")]).frame(width: 160)
                    }
                }
            }
            Text("将鼠标停在英文单词上即可取词，移开后浮窗自动收起。开启组合键后，取词时需同时按住。图片在本机识别。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            Text("自动收藏前，请先登录并选择云词本。仅收藏准确匹配的单词。").font(.system(size: 12)).foregroundStyle(Palette.muted)
        }
    }
    private func modifier(_ label: String, flag: Int) -> some View { Toggle(label, isOn: Binding(get: { preferences.hoverModifiers & flag != 0 }, set: { if $0 { preferences.hoverModifiers |= flag } else { preferences.hoverModifiers &= ~flag } })) }
}

private struct AccountSettings: View {
    @ObservedObject var model: AppModel
    @State private var selected = ""
    @State private var name = ""
    @State private var token = ""
    @State private var importing = false
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("本软件为个人开发的非官方工具，与「墨墨背单词」无隶属关系。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            SettingsCard(title: "当前账号") {
                HStack(alignment: .center, spacing: 12) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(model.activeAccount.map { $0.displayName } ?? "还未登录墨墨账号").font(.system(size: 15, weight: .semibold))
                        Text(model.accountStatus).font(.system(size: 12)).foregroundStyle(Palette.muted)
                    }
                    Spacer()
                    Button("登录或添加账号") { model.loginTarget = nil; model.showLogin = true }.buttonStyle(MemoButtonStyle(accent: true)).tourTarget("login")
                }
            }
            SettingsCard(title: "切换与管理") {
                Text("选择账号即可切换").font(.system(size: 12, weight: .semibold)).foregroundStyle(Palette.muted)
                MemoPicker(title: "切换账号", selection: Binding(get: { selected }, set: { id in
                    selected = id
                    guard let account = model.accounts.first(where: { $0.id == id }), !account.needsLogin else { return }
                    if id != model.activeAccountID || (!model.loggedIn && model.accountStatus != "正在连接") {
                        Task { await model.activate(id) }
                    }
                }), options: model.accounts.map { account in
                    (account.id, account.displayName + (account.needsLogin ? " · 需要登录" : ""))
                }, placeholder: "请选择账号").onChange(of: selected) { id in
                    name = model.accounts.first { $0.id == id }?.displayName ?? ""
                }
                HStack(spacing: 8) {
                    Button("重新登录") { model.loginTarget = model.accounts.first { $0.id == selected }; model.showLogin = true }.disabled(selected.isEmpty)
                    Button("退出登录") { Task { await model.logout() } }.disabled(!model.loggedIn)
                    Button("移除账号") { model.notice = .init(title: "移除账号？", message: "只移除本机登录信息，助记与查词历史会保留。", confirm: "移除", destructive: true, action: { Task { await model.removeAccount(selected); selected = "" } }) }.disabled(selected.isEmpty)
                }.buttonStyle(MemoButtonStyle(compact: true))
                HStack { TextField("账号名称", text: $name).inputFrame(); Button("保存账号名称") { model.renameAccount(selected, name) }.buttonStyle(MemoButtonStyle()).disabled(selected.isEmpty) }
                Text("登录凭证保存在这台 Mac，过期后请重新登录。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            }
            SettingsCard(title: "手动导入授权") {
                DisclosureGroup("导入已有的 API 授权") {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("可从墨墨 App 的「开放 API」复制授权，在你自己的电脑上分别导入同一份有效授权。").font(.footnote).foregroundStyle(.secondary)
                        SecureField("墨墨 API 授权", text: $token).inputFrame()
                        Button(importing ? "正在验证…" : "验证并导入") {
                            Task {
                                importing = true
                                defer { importing = false }
                                do {
                                    try await model.finishLogin(.object(["token": .string(token.trimmed)]), identity: "")
                                    token = ""
                                } catch { model.fail(error) }
                            }
                        }.buttonStyle(MemoButtonStyle(accent: true, compact: true)).disabled(importing || token.trimmed.isEmpty)
                    }.padding(.top, 6)
                }
            }
        }.onAppear { selected = model.activeAccountID; name = model.activeAccount?.displayName ?? "" }
          .onChange(of: model.activeAccountID) { selected = $0 }
    }
}

private struct FeedbackSettings: View {
    @Binding var status: String
    var body: some View {
        VStack(spacing: 12) {
            contact("反馈邮箱", value: "1623255609@qq.com") { Button("写邮件") { open("mailto:1623255609@qq.com?subject=MemoLex%20\(AppPaths.version)%20macOS%20反馈") }; Button("复制邮箱") { copy("1623255609@qq.com") } }
            contact("QQ 交流群", value: "1132371819") { Button("复制群号") { copy("1132371819") } }
            contact("GitHub", value: "SuperRaine / MemoLex") { Button("问题反馈") { open(AppPaths.repository + "/issues/new?template=bug_report.yml") }; Button("功能建议") { open(AppPaths.repository + "/issues/new?template=feature_request.yml") } }
        }.buttonStyle(MemoButtonStyle(compact: true))
    }
    private func contact<Buttons: View>(_ title: String, value: String, @ViewBuilder buttons: () -> Buttons) -> some View { SettingsCard(title: title) { HStack { Text(value).foregroundStyle(Palette.deep).textSelection(.enabled); Spacer(); buttons() } } }
    private func copy(_ value: String) { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(value, forType: .string); status = "已复制。" }
    private func open(_ value: String) { if let url = URL(string: value) { NSWorkspace.shared.open(url) } }
}

private struct AboutSettings: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 15) {
            Text("本软件为个人开发的非官方工具，与「墨墨背单词」无隶属关系。").font(.system(size: 12)).foregroundStyle(Palette.muted).padding(10).frame(maxWidth: .infinity, alignment: .leading).background(Palette.wash, in: RoundedRectangle(cornerRadius: 8))
            Text("如果觉得好用，欢迎为开发者发电，支持后续维护。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            HStack(spacing: 18) { payment("微信支付", asset: "sponsor-wechat.jpg", rect: CGRect(x: 354, y: 438, width: 568, height: 568)); payment("支付宝", asset: "sponsor-alipay.jpg", rect: CGRect(x: 216, y: 643, width: 742, height: 742)) }.frame(height: 300)
        }
    }
    private func payment(_ title: String, asset: String, rect: CGRect) -> some View {
        VStack(spacing: 10) { Text(title).font(.system(size: 14, weight: .semibold)); if let image = paymentImage(asset, rect: rect) { Image(nsImage: image).resizable().interpolation(.none).scaledToFit().padding(12).background(.white) } }.padding(12).frame(maxWidth: .infinity).overlay(RoundedRectangle(cornerRadius: 12).stroke(Palette.border))
    }
    private func paymentImage(_ asset: String, rect: CGRect) -> NSImage? {
        guard let image = NSImage(contentsOf: AppPaths.resources.appendingPathComponent(asset)), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil), let crop = cg.cropping(to: rect) else { return nil }
        let padding = rect.width * 0.1; let size = NSSize(width: rect.width + padding * 2, height: rect.height + padding * 2)
        return NSImage(size: size, flipped: false) { _ in NSColor.white.setFill(); NSRect(origin: .zero, size: size).fill(); NSImage(cgImage: crop, size: rect.size).draw(in: NSRect(x: padding, y: padding, width: rect.width, height: rect.height)); return true }
    }
}
