using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using MemoLex.Core;
using MemoLex.Services;
using MemoLex.Desktop;

namespace MemoLex;

internal static class SettingsDialog
{
    internal const string RepositoryUrl="https://github.com/SuperRaine/MemoLex";
    internal const string HomepageUrl = "https://memolex.de5.net/";
    internal const string QuarkDownloadUrl = HomepageUrl + "quark.html";
    internal const string FeedbackEmail="1623255609@qq.com";
    internal const string FeedbackQqGroup="1132371819";
    internal const string BugReportUrl=RepositoryUrl+"/issues/new?template=bug_report.yml";
    internal const string FeatureRequestUrl=RepositoryUrl+"/issues/new?template=feature_request.yml";
    internal const string IndependenceNotice="本软件为个人开发的非官方工具，与「墨墨背单词」无隶属关系。";
    internal static string Version=>typeof(SettingsDialog).Assembly.GetName().Version?.ToString(3)??"2.6.2";
    [Flags]
    private enum SettingsArea { None=0, General=1, Hover=2, Shortcuts=4 }
    public static void Edit(Window owner,TokenStore store,Preferences preferences,Func<Dictionary<string,string>,string?>? validateShortcuts=null,Func<Task>? accountChanged=null,string? initialPage=null,UpdateManager? updates=null,Action? startOnboarding=null,Action? settingsChanged=null,CacheMaintenanceHost? cacheMaintenance=null)
    {
        var dialog=Build(owner,store,preferences,validateShortcuts,accountChanged:accountChanged,initialPage:initialPage,updates:updates,startOnboarding:startOnboarding,settingsChanged:settingsChanged,cacheMaintenance:cacheMaintenance);
        dialog.ShowDialog();
    }

    internal static Window Build(Window owner,TokenStore store,Preferences preferences,Func<Dictionary<string,string>,string?>? validateShortcuts=null,IStartupRegistration? startupRegistration=null,Func<Task>? accountChanged=null,string? initialPage=null,UpdateManager? updates=null,Action? startOnboarding=null,Action? settingsChanged=null,CacheMaintenanceHost? cacheMaintenance=null)
    {
        var dialog=InputDialog.Shell(owner,"设置",out _);
        dialog.Width=Math.Min(980,SystemParameters.WorkArea.Width-60);dialog.Height=Math.Min(740,SystemParameters.WorkArea.Height-50);
        dialog.MinWidth=680;dialog.MinHeight=520;dialog.SizeToContent=SizeToContent.Manual;dialog.ResizeMode=ResizeMode.CanResize;
        WindowChrome.GetWindowChrome(dialog).ResizeBorderThickness=new Thickness(6);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(172)});body.ColumnDefinitions.Add(new ColumnDefinition());
        InputDialog.SetBody(dialog,body);
        var navigation=new ListBox {Margin=new Thickness(10,16,10,14),BorderThickness=new Thickness(0),Background=Brushes.Transparent,FontSize=13,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var side=new Border {BorderThickness=new Thickness(0,0,1,0),Child=navigation};
        side.SetResourceReference(Border.BackgroundProperty,"SidebarBrush");
        side.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        body.Children.Add(side);
        var right=new Grid();right.RowDefinitions.Add(new RowDefinition());right.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});Grid.SetColumn(right,1);body.Children.Add(right);
        var host=new ContentControl {HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};right.Children.Add(host);
        var status=InputDialog.Text("", status: true);
        status.Visibility=Visibility.Collapsed;status.Margin=new Thickness(20,8,20,12);
        Grid.SetRow(status,1);right.Children.Add(status);
        void SetStatus(string? message)
        {
            status.Text=message??"";
            status.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
        }

        var pages=new List<(string Title,ScrollViewer View)>();
        StackPanel Page(string title,string description)
        {
            var panel=new StackPanel {Margin=new Thickness(20,14,20,12)};
            var pageTitle=new TextBlock {Text=title,FontSize=20,FontWeight=FontWeights.SemiBold};
            pageTitle.SetResourceReference(TextBlock.ForegroundProperty,"InkStrongBrush");
            panel.Children.Add(pageTitle);
            var subtitle=InputDialog.Text(description);subtitle.FontSize=12;subtitle.Margin=new Thickness(0,4,0,14);panel.Children.Add(subtitle);
            var view=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            pages.Add((title,view));return panel;
        }
        ScrollViewer PageView(string title)=>pages.Single(page=>page.Title==title).View;
        var general=Page("常规","设置外观主题、关闭方式、启动选项和考试倒计时。");
        var generalGrid=new Grid();generalGrid.ColumnDefinitions.Add(new());generalGrid.ColumnDefinitions.Add(new());general.Children.Add(generalGrid);
        for(var row=0;row<4;row++)generalGrid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        StackPanel Card(string title,int row,int column,int span,out Border border)
        {
            var content=new StackPanel();
            var cardTitle=new TextBlock{Text=title,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)};
            cardTitle.SetResourceReference(TextBlock.ForegroundProperty,"InkStrongBrush");
            content.Children.Add(cardTitle);
            border=new Border{Child=content,Padding=new Thickness(12),Margin=new Thickness(0,0,0,12),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1)};
            border.SetResourceReference(Border.BackgroundProperty,"CardSoftBrush");
            border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
            Grid.SetRow(border,row);Grid.SetColumn(border,column);Grid.SetColumnSpan(border,span);generalGrid.Children.Add(border);return content;
        }
        var themeCard=Card("外观主题",0,0,2,out _);
        var themeRow=new StackPanel{Orientation=Orientation.Horizontal};
        var themeLight=new RadioButton{Content="浅色模式",GroupName="ThemeMode",IsChecked=preferences.ThemeMode=="light",Margin=new Thickness(0,0,22,0)};
        var themeDark=new RadioButton{Content="深色模式",GroupName="ThemeMode",IsChecked=preferences.ThemeMode=="dark",Margin=new Thickness(0,0,22,0)};
        var themeSystem=new RadioButton{Content="跟随系统",GroupName="ThemeMode",IsChecked=preferences.ThemeMode=="system"};
        themeRow.Children.Add(themeLight);themeRow.Children.Add(themeDark);themeRow.Children.Add(themeSystem);
        themeCard.Children.Add(themeRow);

        var countdownCard=Card("考试倒计时",1,0,2,out _);
        var showCountdown=new CheckBox {Content="在标题栏显示考试倒计时",IsChecked=preferences.ShowExamCountdown,Margin=new Thickness(0,0,0,8)};
        countdownCard.Children.Add(showCountdown);
        var examFields=new WrapPanel();countdownCard.Children.Add(examFields);
        var nameField=new StackPanel{Width=180,Margin=new Thickness(0,0,12,0)};examFields.Children.Add(nameField);nameField.Children.Add(InputDialog.Text("考试名称"));
        var examName=new TextBox {Text=preferences.ExamName,MaxLength=40,Margin=new Thickness(0,4,0,4),Padding=new Thickness(9,6,9,6),Height=36};nameField.Children.Add(examName);
        var dateField=new StackPanel{Width=340};examFields.Children.Add(dateField);dateField.Children.Add(InputDialog.Text("考试日期 · 可选择或输入"));
        var examDate=new DatePartsInput(preferences.ExamDate??DateTime.Today.AddDays(30));
        void EnableCountdownFields(){examName.IsEnabled=examDate.IsEnabled=showCountdown.IsChecked==true;}
        showCountdown.Checked+=(_,_)=>EnableCountdownFields();showCountdown.Unchecked+=(_,_)=>EnableCountdownFields();EnableCountdownFields();
        dateField.Children.Add(examDate);
        dialog.RegisterName("ShowExamCountdown",showCountdown);dialog.RegisterName("ExamName",examName);dialog.RegisterName("ExamDate",examDate);
        dialog.RegisterName("ExamDateYear",examDate.Year);dialog.RegisterName("ExamDateMonth",examDate.Month);dialog.RegisterName("ExamDateDay",examDate.Day);
        var closeCard=Card("关闭窗口",2,0,1,out var closeBorder);
        var tray=new RadioButton {Content="最小化到托盘",GroupName="CloseAction",IsChecked=preferences.CloseToTray,Margin=new Thickness(0,0,0,8)};
        var exit=new RadioButton {Content="退出软件",GroupName="CloseAction",IsChecked=!preferences.CloseToTray,Margin=new Thickness(0,0,0,8)};
        var rememberClose=new CheckBox {Content=new TextBlock{Text="记住关闭方式，下次不再询问",TextWrapping=TextWrapping.Wrap},IsChecked=preferences.RememberCloseAction,Margin=new Thickness(0,2,0,0)};
        closeCard.Children.Add(tray);closeCard.Children.Add(exit);closeCard.Children.Add(rememberClose);
        var launchCard=Card("启动与快捷查词",2,1,1,out var launchBorder);
        void ArrangeGeneralCards()
        {
            var compact=generalGrid.ActualWidth<520;
            Grid.SetColumnSpan(closeBorder,compact?2:1);Grid.SetColumn(launchBorder,compact?0:1);Grid.SetColumnSpan(launchBorder,compact?2:1);Grid.SetRow(launchBorder,compact?3:2);
            closeBorder.Margin=new Thickness(0,0,compact?0:6,12);launchBorder.Margin=new Thickness(compact?0:6,0,0,12);
        }
        generalGrid.SizeChanged+=(_,_)=>ArrangeGeneralCards();ArrangeGeneralCards();
        startupRegistration??=new StartupRegistration();bool? startupEnabled=null;string? startupReadError=null;
        try{startupEnabled=startupRegistration.Enabled;}catch{startupReadError="无法读取开机自启动状态，请检查当前用户权限。";}
        var autoStart=new CheckBox {Content="开机自启动",IsChecked=startupEnabled==true,IsEnabled=startupEnabled.HasValue,Margin=new Thickness(0,0,0,8)};
        launchCard.Children.Add(autoStart);if(startupReadError!=null)launchCard.Children.Add(InputDialog.Text(startupReadError));
        var doubleCopy=new CheckBox {Content=new TextBlock{Text="连按两次 Ctrl+C 查词或翻译",TextWrapping=TextWrapping.Wrap},IsChecked=preferences.DoubleCopyEnabled,Margin=new Thickness(0,4,0,6)};
        dialog.RegisterName("DoubleCopyEnabled",doubleCopy);launchCard.Children.Add(doubleCopy);launchCard.Children.Add(InputDialog.Text("选中文字后，在 750 毫秒内连按两次 Ctrl+C。"));
        var recallMode=new CheckBox {Content=new TextBlock{Text="再次查词时开启「先回想」模式",TextWrapping=TextWrapping.Wrap},IsChecked=preferences.RecallModeEnabled,Margin=new Thickness(0,8,0,6)};
        dialog.RegisterName("RecallModeEnabled",recallMode);launchCard.Children.Add(recallMode);launchCard.Children.Add(InputDialog.Text("开启后，再次查询已查过的单词会先遮住释义 2 秒，按空格或回车随时揭晓。"));
        var guide=new Button{Content="查看新手引导",HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=startOnboarding!=null};guide.Click+=(_,_)=>{dialog.Close();owner.Dispatcher.BeginInvoke(startOnboarding!);};general.Children.Add(guide);dialog.RegisterName("OpenOnboarding",guide);

        var shortcutsPage=Page("快捷键","点击输入框后按下组合键，按 Delete 清除。");
        var shortcutInputs=new Dictionary<string,TextBox>();var shortcutRows=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
        shortcutsPage.Children.Add(new Border
        {
            Child=shortcutRows,Padding=new Thickness(14,12,2,6),Margin=new Thickness(0,0,0,10),
            CornerRadius=new CornerRadius(11),Background=new SolidColorBrush(Color.FromRgb(248,252,255)),
            BorderBrush=new SolidColorBrush(Color.FromRgb(222,236,246)),BorderThickness=new Thickness(1)
        });
        shortcutRows.SizeChanged+=(_,_)=>shortcutRows.Columns=shortcutRows.ActualWidth>=540?2:1;
        foreach(var definition in ShortcutDefinitions.All)
        {
            var row=new Grid {Margin=new Thickness(0,0,12,8)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(142)});
            row.Children.Add(new TextBlock {Text=definition.Label,FontSize=12,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});
            var input=new TextBox {Text=preferences.Shortcuts.GetValueOrDefault(definition.Id,definition.Default),IsReadOnly=true,Padding=new Thickness(9,8,9,8),FontSize=12,ToolTip="按组合键，Delete 清除"};
            input.PreviewKeyDown+=ShortcutGestures.Capture;Grid.SetColumn(input,1);row.Children.Add(input);shortcutRows.Children.Add(row);shortcutInputs[definition.Id]=input;
            dialog.RegisterName("ShortcutInput_"+definition.Id,input);
        }
        shortcutsPage.Children.Add(InputDialog.Text("全局快捷键可在其他软件中使用。鼠标侧键可后退或前进。"));

        var account=Page("墨墨账号","登录墨墨后可使用云词本和学习记录。");
        var accountNotice=InputDialog.Text(IndependenceNotice);accountNotice.FontSize=12;accountNotice.Margin=new Thickness(0,0,0,10);
        account.Children.Add(accountNotice);dialog.RegisterName("AccountDisclaimer",accountNotice);
        account.Children.Add(AccountSettingsPanel.Build(dialog,store,accountChanged??(()=>Task.CompletedTask)));
        void OpenLink(string url,string error){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{SetStatus(error);}}

        var feedback=Page("反馈","遇到问题或有改进建议，欢迎联系我们。");
        WrapPanel FeedbackCard(string title,string description,string value,string buttonLabel,string name,Action action)
        {
            var card=new Grid {Margin=new Thickness(12),ToolTip=description};card.ColumnDefinitions.Add(new());card.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var details=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
            details.Children.Add(new TextBlock {Text=title,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,3)});
            details.Children.Add(new TextBox {Text=value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(0),FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(8,151,203))});card.Children.Add(details);
            var actions=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);card.Children.Add(actions);
            var button=new Button {Content=buttonLabel,Tag=value};button.Click+=(_,_)=>action();actions.Children.Add(button);dialog.RegisterName(name,button);
            feedback.Children.Add(new Border {Child=card,Background=new SolidColorBrush(Color.FromRgb(248,252,255)),BorderBrush=new SolidColorBrush(Color.FromRgb(225,237,247)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,0,10)});
            return actions;
        }
        void CopyContact(string value,string message){try{Clipboard.SetText(value);SetStatus(message);}catch{SetStatus("复制失败，请选中上方文字手动复制。");}}
        var emailActions=FeedbackCard("反馈邮箱","通过邮件反馈问题与建议。",FeedbackEmail,"写邮件","FeedbackEmailButton",()=>OpenLink("mailto:"+FeedbackEmail+"?subject="+Uri.EscapeDataString("墨墨搜词 "+Version+" 反馈"),"无法打开邮件应用，可复制邮箱后手动发送。"));
        var copyEmail=new Button {Content="复制邮箱",Tag=FeedbackEmail,Margin=new Thickness(8,0,0,0)};copyEmail.Click+=(_,_)=>CopyContact(FeedbackEmail,"反馈邮箱已复制。");emailActions.Children.Add(copyEmail);dialog.RegisterName("CopyFeedbackEmail",copyEmail);
        FeedbackCard("QQ 交流群","复制群号，在 QQ 中搜索加入。",FeedbackQqGroup,"复制群号","CopyFeedbackQq",()=>CopyContact(FeedbackQqGroup,"QQ群号已复制。"));
        var issueActions=FeedbackCard("GitHub","提交问题或建议，查看处理进度。",RepositoryUrl+"/issues","问题反馈","FeedbackIssueButton",()=>OpenLink(BugReportUrl,"无法打开浏览器，可复制上方地址访问 Issues。"));
        ((Button)dialog.FindName("FeedbackIssueButton")).Tag=BugReportUrl;
        var feature=new Button{Content="功能建议",Tag=FeatureRequestUrl,Margin=new Thickness(8,0,0,0)};feature.Click+=(_,_)=>OpenLink(FeatureRequestUrl,"无法打开浏览器，可复制上方地址访问 Issues。");issueActions.Children.Add(feature);dialog.RegisterName("FeedbackFeatureButton",feature);

        var support=Page("关于与赞助","墨墨搜词 MemoLex · "+Version);
        var supportView=PageView("关于与赞助");
        var supportLayout=new Grid {Margin=support.Margin};
        supportLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        supportLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        supportLayout.RowDefinitions.Add(new());
        var supportHeading=new StackPanel();
        foreach(var child in support.Children.Cast<UIElement>().ToArray()){support.Children.Remove(child);supportHeading.Children.Add(child);}
        ((FrameworkElement)supportHeading.Children[1]).Margin=new Thickness(0,4,0,10);
        var supportLinks = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var pillStyle = (Style)Application.Current.FindResource("PillButton");
        foreach (var (label, url, name) in new[] {
            ("软件主页", HomepageUrl, "SupportHomepageButton"),
            ("夸克下载", QuarkDownloadUrl, "SupportQuarkButton"),
            ("GitHub", RepositoryUrl, "SupportRepositoryButton")
        })
        {
            var link = new Button { Content = label, Margin = new Thickness(6, 0, 0, 0), Tag = url, Style = pillStyle };
            link.Click += (_, _) => OpenLink(url, "无法打开浏览器，请稍后重试。");
            supportLinks.Children.Add(link);
            dialog.RegisterName(name, link);
        }
        var supportHeader = new DockPanel();
        DockPanel.SetDock(supportLinks, Dock.Right);
        supportHeader.Children.Add(supportLinks);
        supportHeader.Children.Add(supportHeading);
        supportLayout.Children.Add(supportHeader);
        var supportIntro=new StackPanel();Grid.SetRow(supportIntro,1);supportLayout.Children.Add(supportIntro);
        var projectNotice=InputDialog.Text(IndependenceNotice);
        supportIntro.Children.Add(new Border {Child=projectNotice,Padding=new Thickness(8),Margin=new Thickness(0,0,0,8),
            Background=new SolidColorBrush(Color.FromRgb(239,248,255)),CornerRadius=new CornerRadius(8)});
        dialog.RegisterName("ProjectDisclaimer",projectNotice);
        supportIntro.Children.Add(InputDialog.Text("如果觉得好用，欢迎为开发者发电，支持后续维护。"));
        var payments=new Grid {Margin=new Thickness(0,12,0,0)};
        payments.ColumnDefinitions.Add(new());payments.ColumnDefinitions.Add(new());
        Grid.SetRow(payments,2);supportLayout.Children.Add(payments);
        var paymentColumn=0;
        foreach(var (label,asset,name) in new[]{("微信支付","sponsor-wechat.jpg","Wechat"),("支付宝","sponsor-alipay.jpg","Alipay")})
        {
            var cardBody=new Grid();cardBody.RowDefinitions.Add(new(){Height=GridLength.Auto});cardBody.RowDefinitions.Add(new());
            cardBody.Children.Add(new TextBlock {Text=label,FontSize=14,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,8)});
            var code=PaymentCodeImages.Load(asset);
            var picture=new Image {Source=code,Width=code.PixelWidth,Height=code.PixelHeight,Stretch=Stretch.Uniform,SnapsToDevicePixels=true};
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
            var scale=new Viewbox {Child=picture,Stretch=Stretch.Uniform};
            var enlarge=new Button {Content=scale,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Cursor=Cursors.Hand,ToolTip="点击放大收款码"};
            Grid.SetRow(enlarge,1);cardBody.Children.Add(enlarge);
            enlarge.Click+=(_,_)=>BuildPaymentDialog(dialog,label,asset).ShowDialog();
            System.Windows.Automation.AutomationProperties.SetName(enlarge,label+"收款码");
            var card=new Border {Child=cardBody,Padding=new Thickness(12),CornerRadius=new CornerRadius(12),Background=Brushes.White,
                BorderBrush=new SolidColorBrush(Color.FromRgb(222,236,246)),BorderThickness=new Thickness(1),Margin=new Thickness(paymentColumn==0?0:7,0,paymentColumn==0?7:0,0)};
            Grid.SetColumn(card,paymentColumn++);payments.Children.Add(card);
            dialog.RegisterName(name+"PaymentImage",picture);dialog.RegisterName("Open"+name+"Code",enlarge);dialog.RegisterName(name+"PaymentCard",card);
        }
        supportView.Content=supportLayout;
        supportView.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
        supportView.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        var updatePage=Page("软件更新","检查新版本，设置自动更新。");
        var ownedUpdates=updates==null;updates??=new UpdateManager(preferences,()=>{});updatePage.Children.Add(updates.BuildPanel(dialog));
        if(ownedUpdates)dialog.Closed+=(_,_)=>updates.Dispose();
        dialog.RegisterName("SettingsUpdatePage",PageView("软件更新"));
        var cachePage=Page("数据与缓存","按类别查看并清理本机缓存与学习记录；不会退出账号，也不会删除本地助记或云端词本。");
        var cacheSummaryBar=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var clearAllCacheButton=new Button{Content="一键清除全部",Padding=new Thickness(13,6,13,6),FontSize=12};
        DockPanel.SetDock(clearAllCacheButton,Dock.Right);
        var refreshCacheButton=new Button{Content="刷新统计",Style=pillStyle,Padding=new Thickness(11,5,11,5),FontSize=12,Margin=new Thickness(0,0,8,0)};
        DockPanel.SetDock(refreshCacheButton,Dock.Right);
        var cacheOverviewText=new TextBlock{FontSize=12.5,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        cacheOverviewText.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
        cacheSummaryBar.Children.Add(clearAllCacheButton);
        cacheSummaryBar.Children.Add(refreshCacheButton);
        cacheSummaryBar.Children.Add(cacheOverviewText);
        cachePage.Children.Add(cacheSummaryBar);
        var cacheListPanel=new StackPanel();
        cachePage.Children.Add(cacheListPanel);
        var cacheSafetyNote=new Border{CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,4,0,6)};
        cacheSafetyNote.SetResourceReference(Border.BackgroundProperty,"CardSoftBrush");
        cacheSafetyNote.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var cacheSafetyText=new TextBlock{Text="安全说明：以上清理仅影响本机缓存与记录，不会退出墨墨账号，也不会删除本地助记和云词本。",FontSize=11.5,TextWrapping=TextWrapping.Wrap,LineHeight=19};
        cacheSafetyText.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
        cacheSafetyNote.Child=cacheSafetyText;
        cachePage.Children.Add(cacheSafetyNote);
        void RenderCacheCategories()
        {
            cacheListPanel.Children.Clear();
            var items=cacheMaintenance?.LoadCategories()??[];
            var activeCount=items.Count(item=>item.HasData);
            cacheOverviewText.Text=items.Count==0
                ?"当前无法读取缓存信息。"
                :activeCount>0?$"当前有 {activeCount} 类数据可清理":"当前没有可清理的缓存。";
            clearAllCacheButton.IsEnabled=cacheMaintenance!=null&&activeCount>0;
            foreach(var item in items)
            {
                var captured=item;
                var row=new Grid{Margin=new Thickness(13,11,13,11)};
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0)};
                var heading=new WrapPanel{Margin=new Thickness(0,0,0,4)};
                var titleBlock=new TextBlock{Text=captured.Title,FontSize=13.5,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
                titleBlock.SetResourceReference(TextBlock.ForegroundProperty,"InkStrongBrush");
                heading.Children.Add(titleBlock);
                var badge=new Border{CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Padding=new Thickness(7,1.5,7,1.5),VerticalAlignment=VerticalAlignment.Center};
                badge.SetResourceReference(Border.BackgroundProperty,captured.HasData?"PillBgBrush":"CardAltBrush");
                badge.SetResourceReference(Border.BorderBrushProperty,captured.HasData?"PillBorderBrush":"BorderBrush");
                var badgeText=new TextBlock{Text=captured.Summary,FontSize=10.5,FontWeight=captured.HasData?FontWeights.SemiBold:FontWeights.Normal};
                badgeText.SetResourceReference(TextBlock.ForegroundProperty,captured.HasData?"AccentDeepBrush":"SubtleBrush");
                badge.Child=badgeText;
                heading.Children.Add(badge);
                info.Children.Add(heading);
                var desc=new TextBlock{Text=captured.Description,FontSize=11.5,TextWrapping=TextWrapping.Wrap,LineHeight=18};
                desc.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
                info.Children.Add(desc);
                row.Children.Add(info);
                var clearBtn=new Button{Content="清除",Padding=new Thickness(13,5,13,5),FontSize=12,VerticalAlignment=VerticalAlignment.Center,IsEnabled=captured.HasData};
                clearBtn.Click+=(_,_)=>
                {
                    if(cacheMaintenance==null||!captured.HasData)return;
                    if(!InputDialog.Confirm(dialog,"清除"+captured.Title,captured.ConfirmMessage,"清除"))return;
                    SetStatus(cacheMaintenance.ClearCategory(captured.Id));
                    RenderCacheCategories();
                };
                Grid.SetColumn(clearBtn,1);row.Children.Add(clearBtn);
                var card=new Border{Child=row,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,9)};
                card.SetResourceReference(Border.BackgroundProperty,"CardSoftBrush");
                card.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
                cacheListPanel.Children.Add(card);
            }
        }
        refreshCacheButton.Click+=(_,_)=>{RenderCacheCategories();SetStatus("已刷新缓存统计。");};
        clearAllCacheButton.Click+=(_,_)=>
        {
            if(cacheMaintenance==null)return;
            var active=cacheMaintenance.LoadCategories().Where(item=>item.HasData).ToArray();
            if(active.Length==0)return;
            var names=string.Join("、",active.Select(item=>item.Title));
            if(!InputDialog.Confirm(dialog,"清除全部缓存",$"将一并清除以下 {active.Length} 类本机缓存与记录：\n{names}\n\n不会退出墨墨账号，也不会删除本地助记或云端词本。","全部清除"))return;
            SetStatus(cacheMaintenance.ClearAll());
            RenderCacheCategories();
        };
        RenderCacheCategories();
        dialog.RegisterName("SettingsCachePage",PageView("数据与缓存"));
        dialog.RegisterName("ClearAllCacheButton",clearAllCacheButton);
        var hoverPage=Page("鼠标取词","鼠标停在英文单词上时显示释义，移开后自动收起。");hoverPage.Children.Add(HoverSettingsPanel.Build(dialog,preferences,out var applyHover));
        dialog.RegisterName("SettingsHoverPage",PageView("鼠标取词"));
        var pageOrder=new[]{"常规","墨墨账号","鼠标取词","快捷键","数据与缓存","软件更新","反馈","关于与赞助"};
        pages=pageOrder.Select(title=>pages.Single(page=>page.Title==title)).ToList();
        var navRowFactory=new FrameworkElementFactory(typeof(StackPanel));
        navRowFactory.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        navRowFactory.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        var navIconFactory=new FrameworkElementFactory(typeof(TextBlock));
        navIconFactory.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("."){Converter=new SettingsNavIconConverter()});
        navIconFactory.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Segoe MDL2 Assets"));
        navIconFactory.SetValue(TextBlock.FontSizeProperty,14.0);
        navIconFactory.SetValue(FrameworkElement.WidthProperty,20.0);
        navIconFactory.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        navIconFactory.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));
        var navTextFactory=new FrameworkElementFactory(typeof(TextBlock));
        navTextFactory.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("."));
        navTextFactory.SetValue(TextBlock.FontSizeProperty,13.0);
        navTextFactory.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        navRowFactory.AppendChild(navIconFactory);
        navRowFactory.AppendChild(navTextFactory);
        navigation.ItemTemplate=new DataTemplate{VisualTree=navRowFactory};
        foreach(var page in pages)navigation.Items.Add(page.Title);
        navigation.SelectionChanged += (_, _) =>
        {
            if (navigation.SelectedIndex < 0) return;
            var selectedPage = pages[navigation.SelectedIndex];
            if (selectedPage.Title == "数据与缓存") RenderCacheCategories();
            host.Content = selectedPage.View;
            UiMotion.Reveal(host);
        };
        navigation.SelectedIndex=Math.Max(0,pages.FindIndex(page=>page.Title==initialPage));

        var pending=SettingsArea.None;
        var autoSaveTimer=new DispatcherTimer(DispatcherPriority.Background,dialog.Dispatcher) {Interval=TimeSpan.FromMilliseconds(275)};
        void QueueSave(SettingsArea area)
        {
            pending|=area;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }
        void ApplyPending()
        {
            autoSaveTimer.Stop();
            var areas=pending;
            pending=SettingsArea.None;
            if(areas==SettingsArea.None)return;
            var savedValues=false;
            string? error=null;
            if(areas.HasFlag(SettingsArea.General))
            {
                preferences.ThemeMode=themeDark.IsChecked==true?"dark":themeSystem.IsChecked==true?"system":"light";
                preferences.DoubleCopyEnabled=doubleCopy.IsChecked==true;
                preferences.RecallModeEnabled=recallMode.IsChecked==true;
                ThemeManager.Apply(preferences.ThemeMode);
                if(showCountdown.IsChecked!=true)preferences.ShowExamCountdown=false;
                else if(examDate.TryGetDate(out var date,out var dateError)&&date!=null)
                {
                    preferences.ShowExamCountdown=true;
                    preferences.ExamDate=date;
                    preferences.ExamName=string.IsNullOrWhiteSpace(examName.Text)?"考试":examName.Text.Trim();
                }
                else error=dateError??"请选择考试日期。";
                if(startupEnabled.HasValue&&(autoStart.IsChecked==true)!=startupEnabled.Value)
                {
                    try{startupRegistration.SetEnabled(autoStart.IsChecked==true);startupEnabled=autoStart.IsChecked==true;}
                    catch{error="开机自启动设置失败，请检查当前用户权限。";}
                }
                savedValues=true;
            }
            if(areas.HasFlag(SettingsArea.Hover))
            {
                error=applyHover()??error;
                savedValues=true;
            }
            if(areas.HasFlag(SettingsArea.Shortcuts))
            {
                if(!ShortcutGestures.Normalize(shortcutInputs.ToDictionary(item=>item.Key,item=>item.Value.Text),out var shortcuts,out var shortcutError))
                    error=shortcutError;
                else if(validateShortcuts?.Invoke(shortcuts) is {} unavailable)error=unavailable;
                else {preferences.Shortcuts=shortcuts;savedValues=true;}
            }
            if(savedValues)
            {
                if(!preferences.TrySave())error="暂时无法保存设置，请检查本机目录权限。";
                settingsChanged?.Invoke();
            }
            SetStatus(error);
        }
        void Watch(System.Windows.Controls.Primitives.ToggleButton control,SettingsArea area)
        {
            control.Checked+=(_,_)=>QueueSave(area);
            control.Unchecked+=(_,_)=>QueueSave(area);
        }
        void SaveClosingOptions()
        {
            // Updating can shut down the owner before this dialog gets a closing event.
            preferences.CloseToTray=tray.IsChecked==true;
            preferences.RememberCloseAction=rememberClose.IsChecked==true;
            SetStatus(preferences.TrySave()?null:"暂时无法保存关闭方式，请检查本机目录权限。");
        }
        tray.Checked+=(_,_)=>SaveClosingOptions();
        exit.Checked+=(_,_)=>SaveClosingOptions();
        rememberClose.Checked+=(_,_)=>SaveClosingOptions();
        rememberClose.Unchecked+=(_,_)=>SaveClosingOptions();
        foreach(var control in new System.Windows.Controls.Primitives.ToggleButton[]{showCountdown,autoStart,doubleCopy,recallMode,themeLight,themeDark,themeSystem})
            Watch(control,SettingsArea.General);
        examName.TextChanged+=(_,_)=>QueueSave(SettingsArea.General);
        foreach(var part in new[]{examDate.Year,examDate.Month,examDate.Day})
        {
            part.SelectionChanged+=(_,_)=>QueueSave(SettingsArea.General);
            part.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>QueueSave(SettingsArea.General)));
        }
        foreach(var name in new[]{"HoverLookupEnabled","HoverTranslucent","HoverAutoAdd","HoverNearest","HoverCtrl","HoverAlt","HoverShift"})
            Watch((CheckBox)dialog.FindName(name),SettingsArea.Hover);
        ((TextBox)dialog.FindName("HoverDelayMs")).TextChanged+=(_,_)=>QueueSave(SettingsArea.Hover);
        ((ComboBox)dialog.FindName("HoverFontSize")).SelectionChanged+=(_,_)=>QueueSave(SettingsArea.Hover);
        foreach(var input in shortcutInputs.Values)input.TextChanged+=(_,_)=>QueueSave(SettingsArea.Shortcuts);
        autoSaveTimer.Tick+=(_,_)=>ApplyPending();
        dialog.Closing+=(_,_)=>ApplyPending();
        foreach(var (name,control) in new (string,FrameworkElement)[]{("RememberCloseAction",rememberClose),("StartWithWindows",autoStart),("SettingsStatus",status),("SettingsNav",navigation),("SettingsPageHost",host),("SettingsGeneralPage",PageView("常规")),("SettingsShortcutsPage",PageView("快捷键")),("SettingsAccountPage",PageView("墨墨账号")),("SettingsFeedbackPage",PageView("反馈")),("SettingsSupportPage",PageView("关于与赞助")),("SponsorPaymentCards",payments)})dialog.RegisterName(name,control);
        return dialog;
    }

    private sealed class SettingsNavIconConverter : System.Windows.Data.IValueConverter
    {
        private static readonly Dictionary<string, string> Icons = new()
        {
            ["常规"] = "\uE713", ["墨墨账号"] = "\uE77B", ["鼠标取词"] = "\uE8B0",
            ["快捷键"] = "\uE765", ["数据与缓存"] = "\uE74D", ["软件更新"] = "\uE896", ["反馈"] = "\uE90A", ["关于与赞助"] = "\uE946"
        };
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is string key && Icons.TryGetValue(key, out var icon) ? icon : "\uE713";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal static Window BuildPaymentDialog(Window owner,string label,string asset)
    {
        var dialog=InputDialog.Shell(owner,label+" · 为开发者发电",out _);
        dialog.Width=Math.Min(640,SystemParameters.WorkArea.Width-60);dialog.Height=Math.Min(880,SystemParameters.WorkArea.Height-50);dialog.SizeToContent=SizeToContent.Manual;
        var picture=new Image {Source=PaymentCodeImages.Load(asset),Stretch=Stretch.Uniform,Margin=new Thickness(12)};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
        InputDialog.SetBody(dialog,picture);return dialog;
    }
}
