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
                _tourSettings=SettingsDialog.Build(this,_tokens,_preferences,ValidateGlobalShortcut,
                    accountChanged:ReloadAccountAsync,updates:_updates,startOnboarding:StartOnboarding,
                    settingsChanged:ApplySettingsUI);
                _tourSettings.Closed+=(_,_)=>{if(_onboardingTour is {ChangingStep:false})_onboardingTour.Skip();};
            }
            ((ListBox)_tourSettings.FindName("SettingsNav")).SelectedItem=page;
            return new(_tourSettings,(FrameworkElement)_tourSettings.FindName(control));
        }
        async void Finished()
        {
            _onboardingTour=null;var settings=_tourSettings;_tourSettings=null;settings?.Close();
            _preferences.OnboardingCompleted=true;_preferences.Save();_editingSettings=false;_navigating=false;RefreshGlobalShortcut();RefreshHoverLookup();
            QueuePendingUpdateNotice();
            try
            {
                if(origin.Target==ViewTarget.Translation)RestoreTranslationView(origin);
                else if(origin.Target==ViewTarget.ChineseSearch&&origin.Chinese is {} chinese)await SearchChineseAsync(chinese.Query,false,false,chinese);
                else if(ValidWord(origin.Word))await LoadWordAsync(origin.Word,false,false,origin);
            }
            catch(OperationCanceledException){}
        }
        // 新增用户功能时同步补充页面聚焦引导，说明保持简短。
        _onboardingTour=new([
            new("查词与阅读","输入英文或中文查词。点击相关词查看释义，点击例句旁的“朗读”听整句。",()=>MainPage(false,BodyGrid)),
            new("登录与切换账号","在这里登录墨墨。选择已保存的账号即可切换。",()=>SettingsPage("墨墨账号","OfficialLoginButton")),
            new("顽固单词","点击顶部单词查词，右侧箭头可换词。在“全部”中选择排序，也可遮住释义，点击揭示答案。",()=>MainPage(false,StubbornCard)),
            new("收藏与管理云词本","点击“加入云词本”收藏，进入“查看 / 编辑”管理单词和查看释义。",()=>MainPage(false,CloudNotepadCard)),
            new("今日学习","查看待学和复习进度，可搜索单词或遮住释义。选词后确认添加到今日新学。",()=>MainPage(false,TodayPlanButton)),
            new("今日新学","将生词加入墨墨今日新学，添加前请按提示开启手机端同步。",()=>MainPage(false,AddTodayButton)),
            new("段落翻译","输入一段文字，点击“翻译”。",()=>MainPage(true,TranslationInput)),
            new("截图翻译","按 "+ShortcutLabel("screenshot")+"，框选屏幕上的英文即可翻译。",()=>MainPage(true,ScreenshotButton)),
            new("鼠标取词","开启后，将鼠标停在英文单词上即可查看释义。",()=>SettingsPage("鼠标取词","HoverLookupEnabled")),
            new("剪贴板查词","开启后，选中文字连按两次 Ctrl+C，或按 "+ShortcutLabel("clipboard")+" 查询。",()=>SettingsPage("常规","DoubleCopyEnabled")),
            new("考试倒计时","开启后填写考试名称和日期，即可在标题栏查看倒计时。",()=>SettingsPage("常规","ShowExamCountdown")),
            new("主页与下载","在这里打开软件主页，也可以直接去夸克下载新版。",()=>SettingsPage("关于与赞助","SupportQuarkButton"))
        ],Finished,_desktopIntegration);
        _onboardingTour.Start();
    }
    private void StopOnboardingOnExit()
    {
        if(_onboardingTour!=null)_preferences.OnboardingCompleted=true;
        _onboardingTour?.Dispose();_onboardingTour=null;var settings=_tourSettings;_tourSettings=null;settings?.Close();
    }
}
