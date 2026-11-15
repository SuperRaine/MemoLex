using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using MemoLex.Core;
using MemoLex.Services;
using MemoLex.Desktop;

namespace MemoLex;

internal static class SettingsDialog
{
    internal const string RepositoryUrl="https://github.com/SuperRaine/MemoLex";
    internal const string FeedbackEmail="1623255609@qq.com";
    internal const string FeedbackQqGroup="1132371819";
    internal const string IssueUrl=RepositoryUrl+"/issues/new/choose";
    internal const string IndependenceNotice="本软件为个人开发的非官方工具，与「墨墨背单词」无隶属关系。";
    internal static string Version=>typeof(SettingsDialog).Assembly.GetName().Version?.ToString(3)??"1.1.0";
    public static SettingsResult Edit(Window owner,TokenStore store,Preferences preferences,Func<Dictionary<string,string>,string?>? validateShortcuts=null,Func<Task>? accountChanged=null,string? initialPage=null,UpdateManager? updates=null,Action? startOnboarding=null)
    {
        var dialog=Build(owner,store,preferences,out var tokenChanged,validateShortcuts,accountChanged:accountChanged,initialPage:initialPage,updates:updates,startOnboarding:startOnboarding);
        return new SettingsResult(dialog.ShowDialog()==true,tokenChanged());
    }

    internal static Window Build(Window owner,TokenStore store,Preferences preferences,out Func<bool> changed,Func<Dictionary<string,string>,string?>? validateShortcuts=null,IStartupRegistration? startupRegistration=null,Func<Task>? accountChanged=null,string? initialPage=null,UpdateManager? updates=null,Action? startOnboarding=null)
    {
        var dialog=InputDialog.Shell(owner,"设置",out _);
        dialog.Width=Math.Min(840,SystemParameters.WorkArea.Width-60);dialog.Height=Math.Min(740,SystemParameters.WorkArea.Height-50);
        dialog.MinWidth=680;dialog.MinHeight=520;dialog.SizeToContent=SizeToContent.Manual;dialog.ResizeMode=ResizeMode.CanResize;
        WindowChrome.GetWindowChrome(dialog).ResizeBorderThickness=new Thickness(6);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(158)});body.ColumnDefinitions.Add(new ColumnDefinition());
        InputDialog.SetBody(dialog,body);
        var navigation=new ListBox {Margin=new Thickness(10,18,10,14),BorderThickness=new Thickness(0),Background=Brushes.Transparent,FontSize=13,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var side=new Border {Background=new SolidColorBrush(Color.FromRgb(246,250,254)),BorderBrush=new SolidColorBrush(Color.FromRgb(229,237,245)),BorderThickness=new Thickness(0,0,1,0),Child=navigation};body.Children.Add(side);
        var right=new Grid();right.RowDefinitions.Add(new RowDefinition());right.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});Grid.SetColumn(right,1);body.Children.Add(right);
        var host=new ContentControl {HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};right.Children.Add(host);
        var footer=new Grid {Margin=new Thickness(24,10,24,18)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});Grid.SetRow(footer,1);right.Children.Add(footer);
        var status=InputDialog.Text("");status.VerticalAlignment=VerticalAlignment.Center;status.Margin=new Thickness(0,0,12,0);footer.Children.Add(status);
        var save=new Button {Content="保存设置",Style=(Style)Application.Current.FindResource("AccentButton"),Padding=new Thickness(18,9,18,9),IsDefault=true};Grid.SetColumn(save,1);footer.Children.Add(save);

        var pages=new List<(string Title,ScrollViewer View)>();
        StackPanel Page(string title,string description)
        {
            var panel=new StackPanel {Margin=new Thickness(26,22,26,16)};
            panel.Children.Add(new TextBlock {Text=title,FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(38,63,83))});
            var subtitle=InputDialog.Text(description);subtitle.Margin=new Thickness(0,8,0,22);panel.Children.Add(subtitle);
            var view=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            pages.Add((title,view));return panel;
        }
        ScrollViewer PageView(string title)=>pages.Single(page=>page.Title==title).View;
        void Heading(Panel page,string text)=>page.Children.Add(new TextBlock {Text=text,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,12)});
        void Divider(Panel page)=>page.Children.Add(new Separator {Margin=new Thickness(0,16,0,8)});

        var general=Page("常规","让墨墨搜词按你的习惯工作。");
        Heading(general,"考试倒计时");
        var showCountdown=new CheckBox {Content="在标题栏显示考试倒计时",IsChecked=preferences.ShowExamCountdown,Margin=new Thickness(0,0,0,12)};
        general.Children.Add(showCountdown);
        general.Children.Add(InputDialog.Text("考试名称"));
        var examName=new TextBox {Text=preferences.ExamName,MaxLength=40,Margin=new Thickness(0,6,0,12),Padding=new Thickness(9),MinHeight=35};
        general.Children.Add(examName);general.Children.Add(InputDialog.Text("考试日期"));
        var examDate=new DatePartsInput(preferences.ExamDate);
        void EnableCountdownFields(){examName.IsEnabled=examDate.IsEnabled=showCountdown.IsChecked==true;}
        showCountdown.Checked+=(_,_)=>EnableCountdownFields();showCountdown.Unchecked+=(_,_)=>EnableCountdownFields();EnableCountdownFields();
        general.Children.Add(examDate);general.Children.Add(InputDialog.Text("可下拉选择，也可直接输入年月日。"));Divider(general);
        dialog.RegisterName("ShowExamCountdown",showCountdown);dialog.RegisterName("ExamName",examName);dialog.RegisterName("ExamDate",examDate);
        dialog.RegisterName("ExamDateYear",examDate.Year);dialog.RegisterName("ExamDateMonth",examDate.Month);dialog.RegisterName("ExamDateDay",examDate.Day);
        Heading(general,"关闭窗口");
        var tray=new RadioButton {Content="最小化到托盘",GroupName="CloseAction",IsChecked=preferences.CloseToTray,Margin=new Thickness(0,0,0,12)};
        var exit=new RadioButton {Content="直接退出程序",GroupName="CloseAction",IsChecked=!preferences.CloseToTray,Margin=new Thickness(0,0,0,12)};
        var rememberClose=new CheckBox {Content="记住关闭方式，下次不再询问",IsChecked=preferences.RememberCloseAction,Margin=new Thickness(0,2,0,8)};
        general.Children.Add(tray);general.Children.Add(exit);general.Children.Add(rememberClose);
        general.Children.Add(InputDialog.Text("收进托盘后，点击托盘图标或使用全局快捷键恢复窗口。"));Divider(general);
        Heading(general,"启动与快速翻译");
        startupRegistration??=new StartupRegistration();bool? startupEnabled=null;string? startupReadError=null;
        try{startupEnabled=startupRegistration.Enabled;}catch{startupReadError="无法读取开机自启动状态，请检查当前用户权限。";}
        var autoStart=new CheckBox {Content="开机自启动",IsChecked=startupEnabled==true,IsEnabled=startupEnabled.HasValue,Margin=new Thickness(0,0,0,8)};
        general.Children.Add(autoStart);general.Children.Add(InputDialog.Text(startupReadError??"登录 Windows 时自动启动；取消勾选可关闭。"));
        var doubleCopy=new CheckBox {Content="连续按两次 Ctrl+C 自动查词 / 翻译",IsChecked=preferences.DoubleCopyEnabled,Margin=new Thickness(0,20,0,8)};
        dialog.RegisterName("DoubleCopyEnabled",doubleCopy);general.Children.Add(doubleCopy);general.Children.Add(InputDialog.Text("在其他应用选中文本后，750 毫秒内复制两次，唤起窗口查询。单词离线查询；段落自动判断语言，优先使用 DeepL，连接失败自动改用 MyMemory。"));
        Divider(general);var guide=new Button{Content="查看新手引导",HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=startOnboarding!=null};guide.Click+=(_,_)=>{dialog.Close();owner.Dispatcher.BeginInvoke(startOnboarding!);};general.Children.Add(guide);dialog.RegisterName("OpenOnboarding",guide);

        var shortcutsPage=Page("快捷键","点击输入框后按组合键，按 Delete 清除。保存后立即生效。");
        var shortcutInputs=new Dictionary<string,TextBox>();
        foreach(var definition in ShortcutDefinitions.All)
        {
            var row=new Grid {Margin=new Thickness(0,0,0,14)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(160)});
            row.Children.Add(new TextBlock {Text=definition.Label,FontSize=12,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});
            var input=new TextBox {Text=preferences.Shortcuts.GetValueOrDefault(definition.Id,definition.Default),IsReadOnly=true,Padding=new Thickness(9,8,9,8),FontSize=12,ToolTip="按组合键，Delete 清除"};
            input.PreviewKeyDown+=ShortcutGestures.Capture;Grid.SetColumn(input,1);row.Children.Add(input);shortcutsPage.Children.Add(row);shortcutInputs[definition.Id]=input;
            dialog.RegisterName("ShortcutInput_"+definition.Id,input);
        }
        shortcutsPage.Children.Add(InputDialog.Text("标为“全局”的快捷键可从后台或托盘唤起软件。鼠标侧键也可前进、后退。"));

        var tokenChanged=false;
        var account=Page("墨墨账号","官方登录、记住账号，并在多个账号间自由切换。离线查词无需登录。");
        var accountNotice=InputDialog.Text(IndependenceNotice);accountNotice.Margin=new Thickness(0,0,0,16);
        account.Children.Add(accountNotice);dialog.RegisterName("AccountDisclaimer",accountNotice);
        account.Children.Add(AccountSettingsPanel.Build(dialog,store,async()=>{tokenChanged=true;if(accountChanged!=null)await accountChanged();}));
        void OpenLink(string url,string error){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{status.Text=error;}}

        var feedback=Page("反馈","遇到问题或有新想法，欢迎通过以下方式联系。");
        void FeedbackCard(string title,string description,string value,string buttonLabel,string name,Action action)
        {
            var card=new StackPanel {Margin=new Thickness(16)};
            card.Children.Add(new TextBlock {Text=title,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
            card.Children.Add(InputDialog.Text(description));
            var address=new TextBox {Text=value,IsReadOnly=true,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(0,6,0,6),FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(8,151,203))};card.Children.Add(address);
            var button=new Button {Content=buttonLabel,Tag=value,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};
            button.Click+=(_,_)=>action();card.Children.Add(button);dialog.RegisterName(name,button);
            feedback.Children.Add(new Border {Child=card,Background=new SolidColorBrush(Color.FromRgb(248,252,255)),BorderBrush=new SolidColorBrush(Color.FromRgb(225,237,247)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,0,0,14)});
        }
        void CopyContact(string value,string message){try{Clipboard.SetText(value);status.Text=message;}catch{status.Text="复制失败，请选中上方文字手动复制。";}}
        FeedbackCard("反馈邮箱","通过邮件反馈问题与建议。",FeedbackEmail,"写邮件","FeedbackEmailButton",()=>OpenLink("mailto:"+FeedbackEmail+"?subject="+Uri.EscapeDataString("墨墨搜词 "+Version+" 反馈"),"无法打开邮件应用，可复制邮箱后手动发送。"));
        FeedbackCard("QQ 交流群","复制群号，在 QQ 中搜索加入。",FeedbackQqGroup,"复制群号","CopyFeedbackQq",()=>CopyContact(FeedbackQqGroup,"QQ群号已复制。"));
        FeedbackCard("GitHub Issues","提交问题、提出功能建议，跟踪处理进度。",RepositoryUrl+"/issues","提交 Issue","FeedbackIssueButton",()=>OpenLink(IssueUrl,"无法打开浏览器，可复制上方地址访问 Issues。"));
        var copyEmail=new Button {Content="复制反馈邮箱",HorizontalAlignment=HorizontalAlignment.Left};copyEmail.Click+=(_,_)=>CopyContact(FeedbackEmail,"反馈邮箱已复制。");feedback.Children.Add(copyEmail);dialog.RegisterName("CopyFeedbackEmail",copyEmail);

        var support=Page("赞助与关于","墨墨搜词 MemoLex · "+Version);
        var projectNotice=InputDialog.Text(IndependenceNotice);
        support.Children.Add(new Border {Child=projectNotice,Padding=new Thickness(12),Margin=new Thickness(0,0,0,16),
            Background=new SolidColorBrush(Color.FromRgb(239,248,255)),CornerRadius=new CornerRadius(8)});
        dialog.RegisterName("ProjectDisclaimer",projectNotice);
        support.Children.Add(InputDialog.Text("如果墨墨搜词对你有帮助，欢迎自愿赞助，支持后续开发与维护。感谢你的支持！"));
        var payments=new TabControl {Margin=new Thickness(0,16,0,12),BorderThickness=new Thickness(0),Background=Brushes.Transparent};
        foreach(var (label,asset,name) in new[]{("微信支付","sponsor-wechat.jpg","Wechat"),("支付宝","sponsor-alipay.jpg","Alipay")})
        {
            var picture=new Image {Source=new BitmapImage(new Uri("pack://application:,,,/MemoLex;component/Assets/"+asset)),MaxHeight=390,Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);
            var enlarge=new Button {Content=picture,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(6),HorizontalAlignment=HorizontalAlignment.Center,Cursor=Cursors.Hand,ToolTip="点击查看收款码大图"};
            enlarge.Click+=(_,_)=>BuildPaymentDialog(dialog,label,asset).ShowDialog();
            payments.Items.Add(new TabItem {Header=label,Content=enlarge});dialog.RegisterName(name+"PaymentImage",picture);dialog.RegisterName("Open"+name+"Code",enlarge);
        }
        support.Children.Add(payments);support.Children.Add(InputDialog.Text("点击收款码可放大查看。"));Divider(support);
        var sourceLink=new Button {Content="GitHub 开源项目",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};
        sourceLink.Click+=(_,_)=>OpenLink(RepositoryUrl,"无法打开浏览器，请稍后重试。");support.Children.Add(sourceLink);
        var updatePage=Page("软件更新","从 GitHub 获取最新版本，按你的习惯检查更新。");
        var ownedUpdates=updates==null;updates??=new UpdateManager(preferences,()=>{});updatePage.Children.Add(updates.BuildPanel(dialog));
        if(ownedUpdates)dialog.Closed+=(_,_)=>updates.Dispose();
        dialog.RegisterName("SettingsUpdatePage",PageView("软件更新"));
        var hoverPage=Page("鼠标取词","悬停显示释义和助记，离开自动收起。");hoverPage.Children.Add(HoverSettingsPanel.Build(dialog,preferences,out var applyHover));
        dialog.RegisterName("SettingsHoverPage",PageView("鼠标取词"));
        var pageOrder=new[]{"常规","墨墨账号","鼠标取词","快捷键","软件更新","反馈","赞助与关于"};
        pages=pageOrder.Select(title=>pages.Single(page=>page.Title==title)).ToList();
        foreach(var page in pages)navigation.Items.Add(page.Title);
        navigation.SelectionChanged+=(_,_)=>{if(navigation.SelectedIndex>=0)host.Content=pages[navigation.SelectedIndex].View;};
        navigation.SelectedIndex=Math.Max(0,pages.FindIndex(page=>page.Title==initialPage));

        save.Click+=(_,_)=>
        {
            try
            {
                if(!ShortcutGestures.Normalize(shortcutInputs.ToDictionary(item=>item.Key,item=>item.Value.Text),out var shortcuts,out var shortcutError)){status.Text=shortcutError;return;}
                var unavailable=validateShortcuts?.Invoke(shortcuts);if(unavailable!=null){status.Text=unavailable;return;}
                var validExamDate=examDate.TryGetDate(out var selectedExamDate,out var examDateError);
                if(showCountdown.IsChecked==true&&!validExamDate){navigation.SelectedItem="常规";status.Text=examDateError;return;}
                if(!int.TryParse(((TextBox)dialog.FindName("HoverDelayMs")).Text,out var delay)||delay is <100 or >3000){navigation.SelectedItem="鼠标取词";status.Text="取词延迟需为 100–3000 毫秒。";return;}
                if(startupEnabled.HasValue&&(autoStart.IsChecked==true)!=startupEnabled.Value)
                {
                    try{startupRegistration.SetEnabled(autoStart.IsChecked==true);startupEnabled=autoStart.IsChecked==true;}
                    catch{navigation.SelectedItem="常规";status.Text="开机自启动设置失败，请检查当前用户权限后重试。";return;}
                }
                applyHover();preferences.CloseToTray=tray.IsChecked==true;preferences.RememberCloseAction=rememberClose.IsChecked==true;
                preferences.ShowExamCountdown=showCountdown.IsChecked==true;
                preferences.ExamName=string.IsNullOrWhiteSpace(examName.Text)?"考试":examName.Text.Trim();if(validExamDate)preferences.ExamDate=selectedExamDate;
                preferences.DoubleCopyEnabled=doubleCopy.IsChecked==true;preferences.Shortcuts=shortcuts;preferences.Save();dialog.DialogResult=true;
            }
            catch{status.Text="保存失败，请检查输入内容或本机文件权限。";}
        };
        foreach(var (name,control) in new (string,FrameworkElement)[]{("RememberCloseAction",rememberClose),("StartWithWindows",autoStart),("SettingsStatus",status),("SaveSettings",save),("SettingsNav",navigation),("SettingsPageHost",host),("SettingsGeneralPage",PageView("常规")),("SettingsShortcutsPage",PageView("快捷键")),("SettingsAccountPage",PageView("墨墨账号")),("SettingsFeedbackPage",PageView("反馈")),("SettingsSupportPage",PageView("赞助与关于")),("SponsorPaymentTabs",payments)})dialog.RegisterName(name,control);
        changed=()=>tokenChanged;return dialog;
    }

    internal static Window BuildPaymentDialog(Window owner,string label,string asset)
    {
        var dialog=InputDialog.Shell(owner,label+" · 赞助支持",out _);
        dialog.Width=Math.Min(640,SystemParameters.WorkArea.Width-60);dialog.Height=Math.Min(880,SystemParameters.WorkArea.Height-50);dialog.SizeToContent=SizeToContent.Manual;
        var picture=new Image {Source=new BitmapImage(new Uri("pack://application:,,,/MemoLex;component/Assets/"+asset)),Stretch=Stretch.Uniform,Margin=new Thickness(12)};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);
        InputDialog.SetBody(dialog,picture);return dialog;
    }
}
