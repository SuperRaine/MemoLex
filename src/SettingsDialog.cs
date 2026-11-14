using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;

internal static class SettingsDialog
{
    internal const string RepositoryUrl="https://github.com/SuperRaine/MemoLex";
    internal const string FeedbackEmail="1623255609@qq.com";
    internal const string FeedbackQqGroup="1132371819";
    internal const string IssueUrl=RepositoryUrl+"/issues/new/choose";
    internal static string Version=>typeof(SettingsDialog).Assembly.GetName().Version?.ToString(3)??"1.1.0";
    public static SettingsResult Edit(Window owner,TokenStore store,Preferences preferences,Func<Dictionary<string,string>,string?>? validateShortcuts=null)
    {
        var dialog=Build(owner,store,preferences,out var tokenChanged,validateShortcuts);
        return new SettingsResult(dialog.ShowDialog()==true,tokenChanged());
    }

    internal static Window Build(Window owner,TokenStore store,Preferences preferences,out Func<bool> changed,Func<Dictionary<string,string>,string?>? validateShortcuts=null,IStartupRegistration? startupRegistration=null)
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
            pages.Add((title,view));navigation.Items.Add(title);return panel;
        }
        void Heading(Panel page,string text)=>page.Children.Add(new TextBlock {Text=text,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,12)});
        void Divider(Panel page)=>page.Children.Add(new Separator {Margin=new Thickness(0,16,0,8)});

        var general=Page("常规","让墨墨搜词按你的习惯工作。");
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
        general.Children.Add(doubleCopy);general.Children.Add(InputDialog.Text("在其他应用选中文本后，750 毫秒内复制两次，唤起窗口查询。单词离线查询；段落自动判断语言并使用 MyMemory 翻译。"));

        var shortcutsPage=Page("快捷键","点击输入框后按组合键，按 Delete 清除。保存后立即生效。");
        var shortcutInputs=new Dictionary<string,TextBox>();
        foreach(var definition in ShortcutDefinitions.All)
        {
            var row=new Grid {Margin=new Thickness(0,0,0,14)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(160)});
            row.Children.Add(new TextBlock {Text=definition.Label,FontSize=12,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});
            var input=new TextBox {Text=preferences.Shortcuts.GetValueOrDefault(definition.Id,definition.Default),IsReadOnly=true,Padding=new Thickness(9,8,9,8),FontSize=12,ToolTip="按组合键，Delete 清除"};
            input.PreviewKeyDown+=ShortcutGestures.Capture;Grid.SetColumn(input,1);row.Children.Add(input);shortcutsPage.Children.Add(row);shortcutInputs[definition.Id]=input;
        }
        shortcutsPage.Children.Add(InputDialog.Text("标为“全局”的快捷键可从后台或托盘唤起软件。鼠标侧键也可前进、后退。"));

        var account=Page("墨墨账号","连接云词本与个人助记。离线查词无需登录。");
        var connection=new TextBlock {Text=store.HasToken?"已保存账号 Token":"尚未连接墨墨账号",FontSize=15,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(8,151,203)),Margin=new Thickness(0,0,0,14)};account.Children.Add(connection);
        account.Children.Add(InputDialog.Text(store.HasToken?"需要更换时，在下方粘贴新 Token；留空将继续使用当前账号。":"粘贴墨墨开放 API Token，然后保存设置。"));
        var secret=new PasswordBox {Padding=new Thickness(10),FontSize=15,Margin=new Thickness(0,16,0,12),MaxLength=512};account.Children.Add(secret);
        account.Children.Add(InputDialog.Text("Token 使用 Windows 加密保存在当前用户下，仅发送给墨墨官方接口。"));
        void OpenLink(string url,string error){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{status.Text=error;}}
        var tokenLink=new Button {Content="获取墨墨 Token",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,18,0,0)};
        tokenLink.Click+=(_,_)=>OpenLink("https://open.maimemo.com/open/api/v1/tokens/openapi","请在浏览器访问 open.maimemo.com 获取 Token。");account.Children.Add(tokenLink);

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
        navigation.SelectionChanged+=(_,_)=>{if(navigation.SelectedIndex>=0)host.Content=pages[navigation.SelectedIndex].View;};navigation.SelectedIndex=0;

        var tokenChanged=false;
        save.Click+=(_,_)=>
        {
            try
            {
                if(!ShortcutGestures.Normalize(shortcutInputs.ToDictionary(item=>item.Key,item=>item.Value.Text),out var shortcuts,out var shortcutError)){status.Text=shortcutError;return;}
                var unavailable=validateShortcuts?.Invoke(shortcuts);if(unavailable!=null){status.Text=unavailable;return;}
                if(startupEnabled.HasValue&&(autoStart.IsChecked==true)!=startupEnabled.Value)
                {
                    try{startupRegistration.SetEnabled(autoStart.IsChecked==true);startupEnabled=autoStart.IsChecked==true;}
                    catch{navigation.SelectedIndex=0;status.Text="开机自启动设置失败，请检查当前用户权限后重试。";return;}
                }
                if(!string.IsNullOrWhiteSpace(secret.Password)){store.SaveToken(secret.Password.Trim());secret.Clear();tokenChanged=true;}
                preferences.CloseToTray=tray.IsChecked==true;preferences.RememberCloseAction=rememberClose.IsChecked==true;
                preferences.DoubleCopyEnabled=doubleCopy.IsChecked==true;preferences.Shortcuts=shortcuts;preferences.Save();dialog.DialogResult=true;
            }
            catch{status.Text="保存失败，请检查 Token 或本机文件权限。";}
        };
        foreach(var (name,control) in new (string,FrameworkElement)[]{("RememberCloseAction",rememberClose),("StartWithWindows",autoStart),("SettingsStatus",status),("SaveSettings",save),("SettingsNav",navigation),("SettingsPageHost",host),("SettingsGeneralPage",pages[0].View),("SettingsShortcutsPage",pages[1].View),("SettingsAccountPage",pages[2].View),("SettingsFeedbackPage",pages[3].View),("SettingsSupportPage",pages[4].View),("SponsorPaymentTabs",payments)})dialog.RegisterName(name,control);
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
