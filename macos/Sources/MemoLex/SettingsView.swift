import AppKit
import SwiftUI

struct SettingsView: View {
    @ObservedObject var model: AppModel
    @StateObject private var draft: Preferences
    @State private var status = ""
    private let pages = ["常规", "墨墨账号", "鼠标取词", "系统权限", "快捷键", "软件更新", "反馈", "关于与赞助"]
    init(model: AppModel) { self.model = model; _draft = StateObject(wrappedValue: Preferences(copying: model.preferences)) }
    var body: some View {
        VStack(spacing: 0) {
            DialogHeader(title: "设置") { model.sheet = nil }
            HStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 4) { ForEach(pages, id: \.self) { page in Button { model.settingsPage = page } label: { Text(page).font(.system(size: 13, weight: model.settingsPage == page ? .semibold : .regular)).foregroundStyle(model.settingsPage == page ? Palette.deep : Palette.ink).frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 11).padding(.horizontal, 12).background(model.settingsPage == page ? Palette.wash : Color.clear, in: RoundedRectangle(cornerRadius: 8)) }.buttonStyle(.plain) }; Spacer() }.padding(.horizontal, 10).padding(.top, 18).frame(width: 146).background(Palette.canvas)
                Divider()
                VStack(spacing: 0) {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 17) {
                            settingsHeading
                            pageContent
                        }.padding(20).frame(maxWidth: .infinity, alignment: .leading)
                    }.frame(maxHeight: .infinity)
                    HStack { Text(status).font(.system(size: 11)).foregroundStyle(Palette.muted); Spacer(); Button("保存设置") { model.preferences.copyEditable(from: draft); model.savePreferences(); model.sheet = nil }.buttonStyle(MemoButtonStyle(accent: true)) }.padding(.horizontal, 20).padding(.bottom, 13)
                }
            }.frame(maxHeight: .infinity)
        }.frame(width: min(960, (NSScreen.main?.visibleFrame.width ?? 1100) - 70), height: min(710, (NSScreen.main?.visibleFrame.height ?? 820) - 70)).background(.white).foregroundStyle(Palette.ink).font(.system(size: 13)).toggleStyle(MemoCheckStyle()).tint(Palette.deep)
            .overlayPreferenceValue(TourTargets.self) { anchors in TourOverlay(model: model, anchors: anchors, context: "settings") }
            .sheet(isPresented: $model.showLogin) { LoginView(model: model) }
    }
    private var settingsHeading: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 5) { Text(model.settingsPage).font(.system(size: 21, weight: .semibold)); Text(description).font(.system(size: 12)).foregroundStyle(Palette.muted) }
            Spacer()
            if model.settingsPage == "关于与赞助" { Button("GitHub") { open(AppPaths.repository) }.buttonStyle(MemoButtonStyle()) }
        }
    }
    private var description: String {
        switch model.settingsPage {
        case "常规": return "设置关闭方式、启动选项和考试倒计时。"
        case "墨墨账号": return "使用手机号或邮箱登录，也可切换已保存的账号。"
        case "鼠标取词": return "鼠标停在英文单词上时显示释义，移开后自动收起。"
        case "系统权限": return "按需要授权取词、截图和全局快捷键。"
        case "快捷键": return "点击输入框，按下想用的组合键。"
        case "软件更新": return "检查新版本，设置自动更新。"
        case "反馈": return "遇到问题或有改进建议，欢迎联系我们。"
        default: return "墨墨搜词 MemoLex · 1.8.0"
        }
    }
    @ViewBuilder private var pageContent: some View {
        switch model.settingsPage {
        case "常规": GeneralSettings(model: model, preferences: draft)
        case "墨墨账号": AccountSettings(model: model)
        case "鼠标取词": HoverSettings(preferences: draft)
        case "系统权限": PermissionsSettings(model: model)
        case "快捷键": ShortcutSettings(preferences: draft)
        case "软件更新": UpdatesSettings(updates: model.updates)
        case "反馈": FeedbackSettings(status: $status)
        default: AboutSettings(model: model)
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
                HStack(alignment: .top, spacing: 12) {
                    VStack(alignment: .leading, spacing: 6) { Text("考试名称").foregroundStyle(Palette.muted); TextField("考试", text: $preferences.examName).inputFrame() }.frame(width: 180)
                    VStack(alignment: .leading, spacing: 6) { Text("考试日期 · 可选择或输入").foregroundStyle(Palette.muted); DateEntry(date: $preferences.examDate).frame(height: 36) }.frame(maxWidth: .infinity, alignment: .leading)
                }.disabled(!preferences.showCountdown)
            }
            HStack(alignment: .top, spacing: 12) {
                SettingsCard(title: "关闭窗口") {
                    Picker("", selection: $preferences.closeToTray) { Text("收起到菜单栏").tag(true); Text("退出软件").tag(false) }.pickerStyle(.radioGroup).labelsHidden()
                    Toggle("记住关闭方式，下次不再询问", isOn: $preferences.rememberClose)
                }.frame(maxWidth: .infinity)
                SettingsCard(title: "启动与快捷查词") {
                    Toggle("登录 Mac 时自动启动", isOn: $preferences.startAtLogin)
                    Toggle("连按两次 ⌘C 查词或翻译", isOn: $preferences.doubleCopy).tourTarget("clipboard")
                    Text("选中文字后，在 750 毫秒内连按两次 ⌘C。").font(.system(size: 11)).foregroundStyle(Palette.muted)
                }.frame(maxWidth: .infinity)
            }
            Button("查看新手引导") { model.sheet = nil; model.startTour() }.buttonStyle(MemoButtonStyle())
        }
    }
}

struct SettingsCard<Content: View>: View {
    var title: String
    @ViewBuilder var content: Content
    var body: some View { VStack(alignment: .leading, spacing: 11) { Text(title).font(.system(size: 14, weight: .semibold)); content }.padding(13).frame(maxWidth: .infinity, alignment: .leading).background(Palette.canvas.opacity(0.55), in: RoundedRectangle(cornerRadius: 10)).overlay(RoundedRectangle(cornerRadius: 10).stroke(Palette.border)) }
}

private struct HoverSettings: View {
    @ObservedObject var preferences: Preferences
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Grid(alignment: .leading, horizontalSpacing: 25, verticalSpacing: 10) {
                GridRow { Toggle("开启鼠标自动取词", isOn: $preferences.hoverEnabled).tourTarget("hover"); Toggle("取词浮窗半透明", isOn: $preferences.hoverTranslucent) }
                GridRow { Toggle("未找到释义时显示相近单词", isOn: $preferences.hoverNearest); Toggle("找到单词后自动加入云词本", isOn: $preferences.hoverAutoAdd) }
            }
            Grid(alignment: .leading, horizontalSpacing: 20, verticalSpacing: 14) {
                GridRow { Text("取词方式").foregroundStyle(Palette.muted); HStack(spacing: 12) { Text("鼠标悬停 ＋").foregroundStyle(Palette.muted); modifier("⌃", flag: 1); modifier("⌥", flag: 2); modifier("⇧", flag: 4) }.frame(height: 36) }
                GridRow { Text("取词延迟").foregroundStyle(Palette.muted); HStack { TextField("750", value: $preferences.hoverDelay, format: .number).inputFrame().frame(width: 100); Text("毫秒（100–3000）").foregroundStyle(Palette.muted) } }
                GridRow { Text("浮窗字体大小").foregroundStyle(Palette.muted); Picker("", selection: $preferences.hoverFontSize) { Text("较小").tag("small"); Text("标准").tag("standard"); Text("较大").tag("large") }.labelsHidden().frame(width: 180) }
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
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("本软件为个人开发的非官方工具，与「墨墨背单词」无隶属关系。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            Text(model.activeAccount.map { "当前账号：\($0.name)" } ?? "还未登录墨墨账号").font(.system(size: 15, weight: .semibold))
            Button("登录或添加账号") { model.loginTarget = nil; model.showLogin = true }.buttonStyle(MemoButtonStyle(accent: true)).tourTarget("login")
            Text("选择账号即可切换").font(.system(size: 12, weight: .semibold))
            Picker("", selection: $selected) { Text("请选择账号").tag(""); ForEach(model.accounts) { account in Text(account.name + (account.needsLogin ? " · 需要登录" : "")).tag(account.id) } }.labelsHidden().onChange(of: selected) { id in name = model.accounts.first { $0.id == id }?.name ?? ""; if id != model.activeAccountID, model.accounts.contains(where: { $0.id == id && !$0.needsLogin }) { Task { await model.activate(id) } } }
            HStack {
                Button("切换账号") { Task { await model.activate(selected) } }.disabled(selected.isEmpty)
                Button("重新登录") { model.loginTarget = model.accounts.first { $0.id == selected }; model.showLogin = true }.disabled(selected.isEmpty)
                Button("退出登录") { Task { await model.logout() } }.disabled(!model.loggedIn)
                Button("移除账号") { model.notice = .init(title: "移除账号？", message: "只移除本机登录信息，助记与查词历史会保留。", confirm: "移除", destructive: true, action: { Task { await model.removeAccount(selected); selected = "" } }) }.disabled(selected.isEmpty)
            }.buttonStyle(MemoButtonStyle(compact: true))
            HStack { TextField("账号名称", text: $name).inputFrame(); Button("保存账号名称") { model.renameAccount(selected, name) }.buttonStyle(MemoButtonStyle()).disabled(selected.isEmpty) }
            Text(model.accountStatus).font(.system(size: 12)).foregroundStyle(Palette.muted)
            Text("登录信息保存在 Mac 钥匙串中，过期后请重新登录。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            DisclosureGroup("手动导入 Token（高级）") {
                SecureField("Token", text: $token).inputFrame().padding(.vertical, 10)
                Button("验证并导入") { Task { do { let value = token.trimmed; try await model.finishLogin(.object(["token": .string(value)]), identity: "", target: nil); token = "" } catch { model.fail(error) } } }.buttonStyle(MemoButtonStyle()).disabled(token.trimmed.isEmpty)
            }
        }.onAppear { selected = model.activeAccountID; name = model.activeAccount?.name ?? "" }
    }
}

private struct FeedbackSettings: View {
    @Binding var status: String
    var body: some View {
        VStack(spacing: 12) {
            contact("反馈邮箱", value: "1623255609@qq.com") { Button("写邮件") { open("mailto:1623255609@qq.com?subject=MemoLex%201.8.0%20macOS%20反馈") }; Button("复制邮箱") { copy("1623255609@qq.com") } }
            contact("QQ 交流群", value: "1132371819") { Button("复制群号") { copy("1132371819") } }
            contact("GitHub", value: "SuperRaine / MemoLex") { Button("问题反馈") { open(AppPaths.repository + "/issues/new?template=bug_report.yml") }; Button("功能建议") { open(AppPaths.repository + "/issues/new?template=feature_request.yml") } }
        }.buttonStyle(MemoButtonStyle(compact: true))
    }
    private func contact<Buttons: View>(_ title: String, value: String, @ViewBuilder buttons: () -> Buttons) -> some View { SettingsCard(title: title) { HStack { Text(value).foregroundStyle(Palette.deep).textSelection(.enabled); Spacer(); buttons() } } }
    private func copy(_ value: String) { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(value, forType: .string); status = "已复制。" }
    private func open(_ value: String) { if let url = URL(string: value) { NSWorkspace.shared.open(url) } }
}

private struct AboutSettings: View {
    @ObservedObject var model: AppModel
    @State private var keepData = false
    var body: some View {
        VStack(alignment: .leading, spacing: 15) {
            Text("本软件为个人开发的非官方工具，与「墨墨背单词」无隶属关系。").font(.system(size: 12)).foregroundStyle(Palette.muted).padding(10).frame(maxWidth: .infinity, alignment: .leading).background(Palette.wash, in: RoundedRectangle(cornerRadius: 8))
            Text("如果觉得好用，欢迎为开发者发电，支持后续维护。").font(.system(size: 12)).foregroundStyle(Palette.muted)
            HStack(spacing: 18) { payment("微信支付", asset: "sponsor-wechat.jpg", rect: CGRect(x: 354, y: 438, width: 568, height: 568)); payment("支付宝", asset: "sponsor-alipay.jpg", rect: CGRect(x: 216, y: 643, width: 742, height: 742)) }.frame(height: 300)
            DisclosureGroup("卸载软件") {
                VStack(alignment: .leading, spacing: 10) { Toggle("保留用户资料", isOn: $keepData); Text("默认删除本机账号、助记、查词历史和设置，云端数据不受影响。").font(.system(size: 12)).foregroundStyle(Palette.muted); Button("卸载墨墨搜词") { model.notice = .init(title: "卸载墨墨搜词？", message: keepData ? "软件会移到废纸篓，用户资料会保留。" : "软件会移到废纸篓，并删除本机账号、助记、历史和设置。", confirm: "卸载", destructive: true, action: { MacUninstaller.remove(keepData: keepData, model: model) }) }.buttonStyle(MemoButtonStyle()) }.padding(.top, 10)
            }
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

private struct DateEntry: NSViewRepresentable {
    @Binding var date: Date
    func makeNSView(context: Context) -> NSDatePicker { let picker = NSDatePicker(); picker.datePickerStyle = .textFieldAndStepper; picker.datePickerElements = [.yearMonthDay]; picker.datePickerMode = .single; picker.locale = Locale(identifier: "zh_CN"); picker.target = context.coordinator; picker.action = #selector(Coordinator.changed(_:)); picker.dateValue = date; return picker }
    func updateNSView(_ picker: NSDatePicker, context: Context) { picker.dateValue = date; picker.isEnabled = context.environment.isEnabled }
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    final class Coordinator: NSObject { var parent: DateEntry; init(_ parent: DateEntry) { self.parent = parent }; @objc func changed(_ sender: NSDatePicker) { parent.date = sender.dateValue } }
}
