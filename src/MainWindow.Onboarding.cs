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
            new("查词与中文反查","在这里输入英文或中文含义，按 Enter 查询。中文反查会优先显示完整匹配的释义。",()=>MainPage(false,SearchBox)),
            new("释义、助记和例句","这一页可以连续阅读释义、编辑本地助记和查看真题例句。英文单词可点击跳转，再用标题栏后退返回。",()=>MainPage(false,ResultScroll)),
            new("选择自己的云词本","登录后在这里选择目标词本，再用词条页的加入按钮收藏。查看 / 编辑可以管理词条、释义和分组。",()=>MainPage(false,CloudNotepadCard)),
            new("段落翻译","在这一页输入段落后翻译。原文可以修改重译，结果中的英文也能点击查词。",()=>MainPage(true,TranslationInput)),
            new("截图翻译","点击这里或按 "+ShortcutLabel("screenshot")+"，拖动框选英文区域。Esc 取消。图片在本机识别；文字优先发到 DeepL，连接失败改用备用翻译。",()=>MainPage(true,ScreenshotButton)),
            new("连接自己的墨墨账号","在这个页面使用官方登录。账号列表选择后立即切换；不同账号的云端内容和学习状态分别读取。",()=>SettingsPage("墨墨账号","OfficialLoginButton")),
            new("调整鼠标取词","在这个页面调整悬停延迟、组合键和浮窗样式。只识别英文，模糊或不确定的结果会跳过。",()=>SettingsPage("鼠标取词","HoverDelayMs")),
            new("按习惯修改快捷键","在快捷键页面选择对应输入框，再按新的组合键。此处是截图翻译的全局快捷键，平时修改后点击保存设置。",()=>SettingsPage("快捷键","ShortcutInput_screenshot"))
        ],Finished,_desktopIntegration);
        _onboardingTour.Start();
    }
    private void StopOnboardingOnExit()
    {
        if(_onboardingTour!=null)_preferences.OnboardingCompleted=true;
        _onboardingTour?.Dispose();_onboardingTour=null;var settings=_tourSettings;_tourSettings=null;settings?.Close();
    }
}
