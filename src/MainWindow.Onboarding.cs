using System.Windows;
using System.Windows.Controls;

namespace MemoLex;
public partial class MainWindow
{
    private OnboardingTour? _onboardingTour;
    private Window? _tourSettings;
    internal OnboardingTour? ActiveTour=>_onboardingTour;
    internal void StartOnboarding()
    {
        if(_onboardingTour!=null||!_ready||_adding||_refreshing)return;
        var origin=CaptureView();_editingSettings=true;ReleaseGlobalShortcut();if(_hover!=null)_hover.Enabled=false;CancelTranslation();_navigating=true;
        TourSpot MainPage(bool translation,FrameworkElement target)
        {
            var settings=_tourSettings;_tourSettings=null;settings?.Close();
            if(translation)EnterTranslationView();
            else{_viewTarget=ViewTarget.Word;TranslationView.Visibility=ChineseSearchView.Visibility=Visibility.Collapsed;ResultScroll.Visibility=Visibility.Visible;ResultScroll.ScrollToTop();UpdateHeader();}
            return new(this,target);
        }
        TourSpot SettingsPage(string page,string control)
        {
            if(_tourSettings==null)
            {
                _tourSettings=SettingsDialog.Build(this,_tokens,_preferences,out _,ValidateGlobalShortcut,accountChanged:ReloadAccountAsync,updates:_updates,startOnboarding:StartOnboarding);
                _tourSettings.Closed+=(_,_)=>{if(_onboardingTour is {ChangingStep:false})_onboardingTour.Skip();};
            }
            ((ListBox)_tourSettings.FindName("SettingsNav")).SelectedItem=page;
            return new(_tourSettings,(FrameworkElement)_tourSettings.FindName(control));
        }
        async void Finished()
        {
            _onboardingTour=null;var settings=_tourSettings;_tourSettings=null;settings?.Close();
            _preferences.OnboardingCompleted=true;_preferences.Save();_editingSettings=false;_navigating=false;RefreshGlobalShortcut();RefreshHoverLookup();
            try
            {
                if(origin.Target==ViewTarget.Translation)RestoreTranslationView(origin);
                else if(origin.Target==ViewTarget.ChineseSearch&&origin.Chinese is {} chinese)await SearchChineseAsync(chinese.Query,false,false,chinese);
                else if(ValidWord(origin.Word))await LoadWordAsync(origin.Word,false,false,origin);
            }
            catch(OperationCanceledException){}
        }
        _onboardingTour=new([
            new("查词与中文反查","输入英文或中文含义，按 Enter 查询。",()=>MainPage(false,SearchBox)),
            new("阅读词条","听发音、看释义和例句，点击英文单词继续查词。",()=>MainPage(false,ResultScroll)),
            new("编辑助记","点击编辑助记，修改后保存到本地或同步到墨墨。",()=>MainPage(false,EditNotesButton)),
            new("海词词典","点击查看在线词条，海词窗口中也可用浏览器打开。",()=>MainPage(false,HaiciButton)),
            new("历史与前后退","从历史回查单词，用标题栏前进、后退返回阅读位置。",()=>MainPage(false,HistoryButton)),
            new("登录与切换账号","在这里登录墨墨，也可切换已保存的账号。",()=>SettingsPage("墨墨账号","OfficialLoginButton")),
            new("收藏与管理云词本","选择词本收藏单词，点“查看 / 编辑”批量管理词条。",()=>MainPage(false,CloudNotepadCard)),
            new("今日新学","未学单词可加入今日新学，添加前按提示完成手机同步。",()=>MainPage(false,AddTodayButton)),
            new("段落翻译","输入一段文字，点击“翻译”。",()=>MainPage(true,TranslationInput)),
            new("截图翻译","按 "+ShortcutLabel("screenshot")+"，框选要翻译的英文。",()=>MainPage(true,ScreenshotButton)),
            new("鼠标取词","开启后，将鼠标停在英文单词上即可查看释义。",()=>SettingsPage("鼠标取词","HoverLookupEnabled")),
            new("剪贴板查词","选中文字连续按两次 Ctrl+C，或按 "+ShortcutLabel("clipboard")+" 查询。",()=>SettingsPage("常规","DoubleCopyEnabled")),
            new("自定义快捷键","点击输入框，按下新组合键后保存。",()=>SettingsPage("快捷键","ShortcutInput_clipboard")),
            new("考试倒计时","勾选后设置考试名称和日期，在标题栏查看剩余天数。",()=>SettingsPage("常规","ShowExamCountdown")),
            new("桌面设置","可设置开机自启和关闭到托盘，标题栏图钉用于置顶。",()=>SettingsPage("常规","StartWithWindows")),
            new("软件更新","在这里检查新版，也可调整自动更新的频率。",()=>SettingsPage("软件更新","CheckUpdatesButton"))
        ],Finished,_desktopIntegration);
        _onboardingTour.Start();
    }
    private void StopOnboardingOnExit()
    {
        if(_onboardingTour!=null)_preferences.OnboardingCompleted=true;
        _onboardingTour?.Dispose();_onboardingTour=null;var settings=_tourSettings;_tourSettings=null;settings?.Close();
    }
}
