using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;
public partial class MainWindow : Window
{
    private readonly LocalDictionary _dictionary;
    private readonly TokenStore _tokens;
    private readonly MaiMemoClient _api;
    private readonly Preferences _preferences;
    private readonly Dictionary<string, (DateTime Time, Vocabulary? Voc, IReadOnlyList<MemoNote> Notes)> _cloudCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _lookupCts;
    private bool _ready, _changingLists, _adding, _refreshing;
    private string _word = "";
    private Vocabulary? _vocabulary;
    private HwndSource? _source;
    private dynamic? _speaker;
    private TrayIcon? _tray;
    private MemoLex.Desktop.UpdateManager? _updates;
    private bool _exitRequested;
    private readonly bool _allowCloud;
    private readonly bool _desktopIntegration;
    private readonly ExampleSentenceClient _examples;
    private readonly RelatedWords _related;
    private readonly TranslationClient _translation;
    private readonly OnlineDictionaryClient _onlineDictionary;
    private CancellationTokenSource? _examplesCts;
    private readonly List<ExampleSentence> _shownExamples = [];
    private int _exampleVersion, _exampleOffset, _exampleTotal;
    private bool _examplesLoading, _examplesHaveMore, _changingExampleFilter, _examplesLoaded, _examplesError, _initializing;
    private string _displayedPaperType = "ALL";
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow(bool allowCloud = true) : this(Preferences.Load(), new TokenStore(), new LocalDictionary(), new ExampleSentenceClient(), allowCloud, true) { }
    internal MainWindow(Preferences preferences, TokenStore tokens, LocalDictionary dictionary, ExampleSentenceClient examples, bool allowCloud = true, bool desktopIntegration = false, MaiMemoClient? api = null, RelatedWords? related = null, TranslationClient? translation = null, ChineseReverseSearch? chinese = null,MnemonicStore? mnemonics=null,MemoLex.Desktop.UpdateManager? updates=null)
    {
        _preferences = preferences;
        _tokens = tokens;
        _dictionary = dictionary;
        _examples = examples;
        _allowCloud = allowCloud;
        _desktopIntegration=desktopIntegration;
        _updates=updates;
        _api = api ?? new MaiMemoClient(_tokens);
        _accountCredentialKey=_tokens.CredentialKey;_api.ExpectedCredentialKey=_accountCredentialKey;_api.AuthenticationResult+=OnApiAuthenticationResult;
        _related=related??new RelatedWords(_dictionary);
        _translation=translation??new TranslationClient();
        _onlineDictionary = new OnlineDictionaryClient(_translation);
        _chinese=chinese??new ChineseReverseSearch(_dictionary);
        _mnemonics=mnemonics??new MnemonicStore();
        InitializeComponent();
        RestoreStubbornWords();
        Activated+=(_,_)=>{if(_ready)_=RefreshStubbornWordsAsync();};
        RoundedWindow.Attach(this);
        RootFrame.SizeChanged+=(_,e)=>UpdateResponsiveLayout(e.NewSize);
        UpdateShortcutHints();
        ExamplePaperCombo.ItemsSource = PaperTypeChoice.All;
        ExamplePaperCombo.SelectedItem = PaperTypeChoice.All.FirstOrDefault(type => type.Value == _preferences.ExamplePaperType) ?? PaperTypeChoice.All[0];
        Topmost = _preferences.AlwaysOnTop;
        UpdatePinAppearance();
        SetAccountStatus(!_allowCloud ? "离线模式" : _tokens.HasToken ? "连接中" : "未连接");
        AddButton.IsEnabled = false;
        AddTodayButton.IsEnabled = false;
        SpeakButton.IsEnabled = false;
        HaiciButton.IsEnabled=false;
        UpdateExamCountdown();
        Loaded += async (_, _) =>
        {
            StartUpdateChecks();
            StartExamCountdownClock();StartTodayStudyClock();StartAccountClock();
            await InitializeAsync();RefreshHoverLookup();
            if(_desktopIntegration&&_updateNotice==null&&!_preferences.OnboardingCompleted)StartOnboarding();
        };
        SourceInitialized += (_, _) =>
        {
            InstallHotkey();
            if (!desktopIntegration) return;
            RefreshDoubleCopy();
            try { _tray = new TrayIcon(RestoreWindow, () => { RestoreWindow(); SettingsButton_Click(this, new RoutedEventArgs()); }, ExitApplication); }
            catch { StatusText.Text = "托盘暂不可用，关闭窗口时将直接退出。"; }
        };
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) {_restoreState = WindowState;QueuePendingUpdateNotice();} UpdateWindowChrome(); };
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += Window_PreviewMouseDown;
        Closed += (_, _) =>
        {
            StopOnboardingOnExit();
            _examCountdownTimer?.Stop();
            _doubleCopy?.Dispose();_clipboardCts?.Cancel();
            _hover?.Dispose();_accountTimer?.Stop();_accountWatcher?.Dispose();_updates?.Dispose();
            _studyCts?.Cancel();
            _stubbornCts?.Cancel();_stubbornDialog?.Close();
            _studyLoadCts?.Cancel();_accountCts.Cancel();_api.AuthenticationResult-=OnApiAuthenticationResult;
            _studyDayTimer?.Stop();
            _lookupCts?.Cancel();
            _notesCts?.Cancel();
            _examplesCts?.Cancel();
            _tray?.Dispose();
            _preferences.Save();
            ReleaseGlobalShortcut();
            _source?.RemoveHook(HotkeyHook);
            _dictionary.Dispose();
            _api.Dispose();
            _examples.Dispose();
            _related.Dispose();
            _translationCts?.Cancel();
            _onlineDictionary.Dispose();
            _translation.Dispose();
            _chineseCts?.Cancel();_suggestionCts?.Cancel();_chinese.Dispose();
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _exitRequested || _tray == null) return;
        var closeToTray=ResolveCloseAction();
        if(closeToTray==null){e.Cancel=true;return;}
        if(!closeToTray.Value)return;
        e.Cancel = true;
        _preferences.Save();
        Hide();
        _tray.ShowHiddenHint(ShortcutLabel("clipboard"));
    }
    internal Func<Window,Preferences,CloseChoice?> ChooseCloseAction {get;set;}=CloseBehaviorDialog.Ask;
    internal bool? ResolveCloseAction()
    {
        if(!_preferences.RememberCloseAction)
        {
            var choice=ChooseCloseAction(this,_preferences);
            if(choice==null)return null;
            _preferences.CloseToTray=choice.Value.CloseToTray;
            _preferences.RememberCloseAction=choice.Value.Remember;
            _preferences.Save();
        }
        return _preferences.CloseToTray;
    }
    internal void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        if(_desktopIntegration)
        {
            Activate();
            new ForegroundWindow().Activate(new WindowInteropHelper(this).Handle,Topmost);
            SearchBox.Focus();
        }
        SearchBox.SelectAll();
        QueuePendingUpdateNotice();
    }
    internal void ExitApplication()
    {
        _exitRequested = true;
        System.Windows.Application.Current.Shutdown();
    }

    internal async Task InitializeAsync()
    {
        if (_ready || _initializing) return;
        _initializing = true;
        try
        {
            await _dictionary.LoadAsync();
            await _mnemonics.LoadAsync();
            await _related.LoadAsync();
            await _chinese.LoadAsync();
            _ready = true;
            _ = RefreshStubbornWordsAsync();
            DictionaryCountText.Text=$"内置词库 · {_dictionary.Count:N0} 词目";
            UpdateHistory();
            SearchBox.Text = _preferences.History.FirstOrDefault() ?? "serendipity";
            SearchBox.Focus();
            SearchBox.SelectAll();
            await Task.WhenAll(SearchAsync(SearchBox.Text, recordHistory: false), RefreshNotepadsAsync());
        }
        catch (IOException)
        {
            StatusText.Text = "词库文件缺失，请将完整 MemoLex 文件夹解压后运行。";
            DefinitionText.Text = "无法打开离线词库。请确保 data 文件夹与 MemoLex.exe 位于同一目录。";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _initializing = false; }
    }

    private static bool ValidWord(string word) => word.Length is > 0 and <= 100 && Regex.IsMatch(word, @"^[a-zA-Z][a-zA-Z .’'\-]*$");
    private async Task LoadWordAsync(string raw, bool recordHistory = true, bool addNavigationEntry = true, ViewState? restore = null)
    {
        if (!_ready) return;
        var word = ExampleText.NormalizeLookup(raw).Trim();
        if (!ValidWord(word)) { StatusText.Text = "请输入英文单词或短语（最多 100 字符）。"; return; }
        if (addNavigationEntry) RememberCurrentView();
        CancelChineseSearch();
        CancelTranslation();
        _lookupCts?.Cancel();
        _lookupCts?.Dispose();
        _lookupCts = new CancellationTokenSource();
        var cancellation = _lookupCts.Token;
        var wordVersion = ++_wordVersion;
        _navigating = true;
        _word = word;
        _viewTarget = restore?.Target ?? ViewTarget.Word;
        ResultScroll.Visibility=Visibility.Visible;
        TranslationView.Visibility=Visibility.Collapsed;
        ChineseSearchView.Visibility=Visibility.Collapsed;
        if(restore!=null){SynonymsToggle.IsChecked=restore.SynonymsOpen;SimilarToggle.IsChecked=restore.SimilarOpen;DerivativesToggle.IsChecked=restore.DerivativesOpen;PhrasesToggle.IsChecked=restore.PhrasesOpen;}
        if (restore != null)
        {
            _changingExampleFilter = true;
            ExamplePaperCombo.SelectedItem = PaperTypeChoice.All.FirstOrDefault(type => type.Value == restore.Examples.PaperType) ?? PaperTypeChoice.All[0];
            _changingExampleFilter = false;
            _preferences.ExamplePaperType = SelectedPaperType;
            _preferences.Save();
        }
        SearchBox.Text = word;
        UpdateHeader();
        _vocabulary = null;
        AddButton.Content = "＋ 加入云词本";
        AddButton.IsEnabled = !_adding;
        PrepareTodayStudyLookup(word,cancellation);
        SpeakButton.IsEnabled = true;
        HaiciButton.IsEnabled=true;
        WordText.ContextMenu=HaiciWordMenu(word);
        var entry = _dictionary.Lookup(word);
        var localMeaning = entry?.translation ?? "";
        var hasLocalMeaning = !string.IsNullOrWhiteSpace(localMeaning);
        var lookOnline = !hasLocalMeaning && _allowCloud;
        WordText.Text = entry?.word ?? word;
        PhoneticText.Text = string.IsNullOrWhiteSpace(entry?.phonetic) ? "" : $"/{entry.phonetic.Trim('/')}/";
        FillWordLinks(DefinitionText, hasLocalMeaning ? localMeaning : lookOnline ? "正在联网查找释义…" : "内置词库暂无释义。",
            EmptyMatches, word, skipPartOfSpeech: true);
        var exchange = FormatExchange(entry?.exchange);
        FillWordLinks(ExchangeText, exchange, EmptyMatches, word);
        ExchangeCard.Visibility = exchange.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        WordMetaText.Text = hasLocalMeaning ? "内置词库 · 离线可用" : "";
        if (hasLocalMeaning && int.TryParse(entry?.bnc, out var frequency) && frequency > 0) WordMetaText.Text += $"  /  BNC 词频 #{frequency:N0}";
        RenderTags(entry);
        ResultScroll.ScrollToTop();
        ResetExampleView();
        if (recordHistory)
        {
            _preferences.RecordLookup(entry?.word ?? word);
            _preferences.Save();
            UpdateHistory();
        }
        _navigating = false;
        if (addNavigationEntry) _navigation.Navigate(CaptureView());
        UpdateNavigationButtons();
        var notesTask = LoadNotesAsync(word, cancellation);
        var relationsTask=LoadRelationsAsync(word,wordVersion,cancellation);
        var membershipTask=UpdateMembershipAsync();
        var examplesTask = restore?.Examples.Ready == true ? RestoreExamplesAsync(restore.Examples) : LoadExamplesAsync(reset: true);
        var definitionTask = lookOnline ? LoadOnlineDefinitionAsync(word, wordVersion, cancellation) : Task.CompletedTask;
        await Task.WhenAll(notesTask, examplesTask,relationsTask,membershipTask,definitionTask);
        if (wordVersion != _wordVersion) return;
        if (restore != null)
            QueueViewRestore(wordVersion, () =>
            {
                ResultScroll.UpdateLayout();
                ResultScroll.ScrollToVerticalOffset(restore.ScrollOffset);
            });
        else RememberCurrentView();
    }

    private async Task LoadOnlineDefinitionAsync(string word, int version, CancellationToken cancellation)
    {
        OnlineDefinition? definition;
        try { definition = await _onlineDictionary.LookupAsync(word, cancellation); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        if (version != _wordVersion || cancellation.IsCancellationRequested) return;

        FillWordLinks(DefinitionText, definition?.Meaning ?? "暂未找到中文释义，可打开海词词典继续查找。",
            EmptyMatches, word, skipPartOfSpeech: true);
        StatusText.Text = definition == null ? "暂未找到中文释义。" : "已补充联网释义。";
        if (definition == null) return;

        WordMetaText.Text = "联网翻译 · " + definition.Provider;
        if (!Uri.TryCreate(definition.SourceUrl, UriKind.Absolute, out var source)) return;
        var link = new Hyperlink(new Run("维基词典译文")) { NavigateUri = source, ToolTip = "查看原词条（CC BY-SA 4.0）" };
        link.RequestNavigate += (_, args) =>
        {
            try { Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Win32Exception) { StatusText.Text = "暂时无法打开浏览器。"; }
            args.Handled = true;
        };
        WordMetaText.Inlines.Clear();
        WordMetaText.Inlines.Add(link);
        WordMetaText.Inlines.Add(new Run(" · " + definition.Provider));
    }

    private Task LoadNotesAsync(string word,CancellationToken cancellation)=>LoadLocalAndCloudNotesAsync(word,cancellation);

    private string SelectedPaperType => (ExamplePaperCombo.SelectedItem as PaperTypeChoice)?.Value ?? "ALL";
    private void ResetExampleView()
    {
        _displayedPaperType = SelectedPaperType;
        _shownExamples.Clear();
        ExamplesPanel.Children.Clear();
        _exampleOffset = _exampleTotal = 0;
        _examplesHaveMore = _examplesLoaded = _examplesError = false;
        MoreExamplesButton.Visibility = RetryExamplesButton.Visibility = Visibility.Collapsed;
    }
    private async Task LoadExamplesAsync(bool reset, bool recordFilterNavigation = false)
    {
        if (!_ready || !ValidWord(_word) || (!reset && _examplesLoading)) return;
        _examplesCts?.Cancel();
        _examplesCts?.Dispose();
        _examplesCts = CancellationTokenSource.CreateLinkedTokenSource(_lookupCts?.Token ?? CancellationToken.None);
        var cancellation = _examplesCts.Token;
        var version = ++_exampleVersion;
        var wordVersion = _wordVersion;
        var word = _word;
        var paperType = SelectedPaperType;
        if (reset)
        {
            if (recordFilterNavigation) RememberCurrentView();
            ResetExampleView();
            if (recordFilterNavigation) { _viewTarget = ViewTarget.Examples; _navigation.Navigate(CaptureView()); UpdateNavigationButtons(); }
        }
        MoreExamplesButton.Visibility = Visibility.Collapsed;
        RetryExamplesButton.Visibility = Visibility.Collapsed;
        if (!_allowCloud)
        {
            _examplesLoading = false;
            _examplesLoaded = true;
            ExamplesHintText.Text = "离线模式 · 真题例句需要联网查询。";
            if (recordFilterNavigation) QueueExamplesScroll(wordVersion, version);
            return;
        }
        _examplesLoading = true;
        ExamplesHintText.Text = reset ? $"正在查找「{word}」的相关真题…" : "正在加载更多例句…";
        try
        {
            var page = await _examples.SearchAsync(word, paperType, _exampleOffset, cancellationToken: cancellation);
            if (cancellation.IsCancellationRequested || version != _exampleVersion) return;
            foreach (var sentence in page.Items)
            {
                if (_shownExamples.Any(item => sentence.Id.Length > 0 ? item.Id == sentence.Id : item.English == sentence.English && item.Source == sentence.Source)) continue;
                _shownExamples.Add(sentence);
                RenderExample(sentence, word);
            }
            _exampleOffset = page.Offset + page.ReceivedCount;
            _exampleTotal = page.Total;
            _examplesHaveMore = page.HasMore;
            _examplesLoaded = true;
            _examplesError = false;
            ExamplesHintText.Text = _shownExamples.Count == 0 ? "暂未找到相关例句，可切换考试类型再试。" : $"已显示 {_shownExamples.Count} 条 · 共 {page.Total} 条相关结果";
            MoreExamplesButton.Visibility = _examplesHaveMore ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && version == _exampleVersion)
            {
                ExamplesHintText.Text = ex is ExampleLookupException ? ex.Message : "未能加载例句，请点击「重试」。";
                _examplesLoaded = _examplesError = true;
                RetryExamplesButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (version == _exampleVersion)
            {
                _examplesLoading = false;
                RememberCurrentView();
                if (recordFilterNavigation) QueueExamplesScroll(wordVersion, version);
            }
        }
    }
    private void RenderExample(ExampleSentence sentence, string sourceWord)
    {
        var matches=new HashSet<string>(sentence.MatchedWords,StringComparer.OrdinalIgnoreCase);
        foreach(var part in ExampleText.Tokenize(sourceWord))if(part.LookupWord!=null)matches.Add(part.LookupWord);
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(sentence.Source) ? "真题来源未提供" : sentence.Source,
            FontSize = 11, Foreground = Brush("#8DA1B4"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 11) });
        if (sentence.IsOption)
        {
            card.Children.Add(new TextBlock { Text = $"原题选项 {sentence.OptionLabel}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("#AD7B2E"), Margin = new Thickness(0, 0, 0, 7) });
            if (sentence.ContextEnglish.Length > 0)
            {
                card.Children.Add(BuildSentenceText(sentence.ContextEnglish, matches, sourceWord, 14));
                card.Children.Add(new TextBlock { Text = "选项", FontSize = 11, Foreground = Brush("#91A7BA"), Margin = new Thickness(0, 10, 0, 4) });
            }
        }
        card.Children.Add(BuildSentenceText(sentence.English, matches, sourceWord));
        if (sentence.Translation.Length > 0)
            card.Children.Add(BuildExampleTranslation(sentence.Translation,sentence.English,sourceWord,matches,sentence.MatchedTranslations,sentence.TranslationAligned));
        if (sentence.IsOption && sentence.ContextTranslation.Length > 0)
            card.Children.Add(BuildExampleTranslation("原题译文："+sentence.ContextTranslation,sentence.ContextEnglish,sourceWord,matches,sentence.MatchedTranslations));
        ExamplesPanel.Children.Add(new Border { Child = card, Background = Brushes.White, BorderBrush = Brush("#E2EBF3"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(20, 17, 20, 18), Margin = new Thickness(0, 0, 0, 12) });
    }
    private static readonly IReadOnlySet<string> EmptyMatches = new HashSet<string>();
    private static readonly HashSet<string> PartOfSpeechLabels = new(StringComparer.OrdinalIgnoreCase) { "n", "v", "vt", "vi", "adj", "adv", "prep", "pron", "conj", "art", "aux", "abbr", "pl" };
    private TextBlock BuildSentenceText(string text, IReadOnlySet<string> matches, string sourceWord, double fontSize = 16)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = fontSize, LineHeight = 28, Foreground = Brush("#426B89") };
        FillWordLinks(block, text, matches, sourceWord);
        return block;
    }
    private void FillWordLinks(TextBlock block, string text, IReadOnlySet<string> matches, string sourceWord, bool skipPartOfSpeech = false, Func<string,Task>? navigate = null, bool clear = true)
    {
        if(clear)block.Inlines.Clear();
        var parts = ExampleText.Tokenize(text);
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var isLabel = skipPartOfSpeech && part.LookupWord != null && PartOfSpeechLabels.Contains(part.LookupWord) && index + 1 < parts.Count && parts[index + 1].Text.StartsWith('.');
            if (part.LookupWord == null || isLabel) { block.Inlines.Add(new Run(part.Text)); continue; }
            var word = part.LookupWord;
            var highlighted = matches.Contains(word);
            var link = new Hyperlink(new Run(part.Text)) { Foreground = Brush(highlighted ? "#087FAF" : "#426B89"), TextDecorations = null,
                Background=highlighted?Brush("#E1F4FF"):Brushes.Transparent,
                FontWeight = highlighted ? FontWeights.SemiBold : FontWeights.Normal, ToolTip = "查询 " + word, Cursor = Cursors.Hand };
            link.ContextMenu=HaiciWordMenu(word);
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;
            link.Click += async (_, _) => {if(navigate!=null)await navigate(word);else await NavigateLinkedWordAsync(word, sourceWord);};
            block.Inlines.Add(link);
        }
    }
    private TextBlock BuildExampleTranslation(string text,string english,string sourceWord,IReadOnlySet<string> matches,IReadOnlySet<string>? explicitTerms,bool aligned=true)
    {
        string? Meaning(string word)
        {
            var entry=_dictionary.Lookup(word);var values=new List<string>();
            if(!string.IsNullOrWhiteSpace(entry?.translation))values.Add(entry.translation);
            foreach(var item in (entry?.exchange??"").Split('/'))
                if(item.StartsWith("0:",StringComparison.Ordinal)&&_dictionary.Lookup(item[2..])?.translation is {Length:>0} definition)values.Add(definition);
            if((word.EndsWith("'s",StringComparison.OrdinalIgnoreCase)||word.EndsWith("’s",StringComparison.OrdinalIgnoreCase))&&_dictionary.Lookup(word[..^2])?.translation is {Length:>0} possessiveBase)values.Add(possessiveBase);
            return values.Count==0?null:string.Join('\n',values);
        }
        var terms=TextHighlights.TranslationTerms(english,text,sourceWord,matches,Meaning,explicitTerms,aligned);
        var block=new TextBlock { TextWrapping=TextWrapping.Wrap,FontSize=13,LineHeight=23,Foreground=Brush("#7D93A6"),Margin=new Thickness(0,11,0,0) };
        foreach(var part in TextHighlights.Split(text,terms))
            if(part.Highlighted)block.Inlines.Add(new Run(part.Text){Background=Brush("#DEF4FF"),Foreground=Brush("#087FAF"),FontWeight=FontWeights.SemiBold});
            else FillWordLinks(block,part.Text,matches,sourceWord,clear:false);
        return block;
    }
    private async Task NavigateLinkedWordAsync(string word, string sourceWord)
    {
        if (!sourceWord.Equals(_word, StringComparison.OrdinalIgnoreCase)) return;
        word = ExampleText.ResolveLookupWord(word, spelling => _dictionary.Lookup(spelling) != null);
        if (!ValidWord(word)) { StatusText.Text = "暂时无法查询这个拼写，请在搜索框输入英文单词。"; return; }
        await LoadWordAsync(word);
    }
    private Task RestoreExamplesAsync(ExampleSnapshot snapshot)
    {
        _examplesCts?.Cancel();
        ++_exampleVersion;
        _examplesLoading = false;
        _changingExampleFilter = true;
        ExamplePaperCombo.SelectedItem = PaperTypeChoice.All.First(type => type.Value == snapshot.PaperType);
        _changingExampleFilter = false;
        _preferences.ExamplePaperType = snapshot.PaperType;
        _preferences.Save();
        _displayedPaperType = snapshot.PaperType;
        _shownExamples.Clear();
        _shownExamples.AddRange(snapshot.Items);
        _exampleOffset = snapshot.Offset;
        _exampleTotal = snapshot.Total;
        _examplesHaveMore = snapshot.HaveMore;
        _examplesLoaded = snapshot.Ready;
        _examplesError = snapshot.CanRetry;
        ExamplesPanel.Children.Clear();
        foreach (var sentence in snapshot.Items) RenderExample(sentence, _word);
        ExamplesHintText.Text = snapshot.Hint;
        MoreExamplesButton.Visibility = snapshot.HaveMore && !snapshot.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        RetryExamplesButton.Visibility = snapshot.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        return Task.CompletedTask;
    }
    private async void ExamplePaperCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _changingExampleFilter) return;
        _preferences.ExamplePaperType = SelectedPaperType;
        _preferences.Save();
        await LoadExamplesAsync(reset: true, recordFilterNavigation: true);
    }
    private async void MoreExamplesButton_Click(object sender, RoutedEventArgs e) => await LoadExamplesAsync(reset: false);
    private async void RetryExamplesButton_Click(object sender, RoutedEventArgs e) => await LoadExamplesAsync(reset: _shownExamples.Count == 0);

    private void RenderTags(DictionaryEntry? entry)
    {
        TagsPanel.Children.Clear();
        var names = new Dictionary<string, string> { ["zk"] = "中考", ["gk"] = "高考", ["cet4"] = "CET-4", ["cet6"] = "CET-6", ["ky"] = "考研", ["toefl"] = "TOEFL", ["ielts"] = "IELTS", ["gre"] = "GRE" };
        foreach (var tag in (entry?.tag ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            AddTag(names.GetValueOrDefault(tag, tag.ToUpperInvariant()), tag switch { "cet4" => "CET4", "cet6" => "CET6", "ky" => "POSTGRADUATE", "gk" => "COLLEGE_ENTRANCE_EXAM", _ => null });
        if (entry?.oxford == "1") AddTag("核心词汇");
        if (int.TryParse(entry?.collins, out var stars) && stars > 0) AddTag(new string('★', Math.Min(5, stars)));
    }
    private void AddTag(string text, string? paperType = null)
    {
        var label = new TextBlock { Text = text, FontSize = 10, Foreground = Brush("#658DA8") };
        if (paperType == null)
            TagsPanel.Children.Add(new Border { Background = Brush("#EDF6FC"), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 5), Child = label });
        else
        {
            var button = new Button { Content = label, Background = Brush("#E7F6FE"), BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 5), ToolTip = $"查看{text}真题例句" };
            button.Click += async (_, _) => await OpenExamExamplesAsync(paperType);
            TagsPanel.Children.Add(button);
        }
    }
    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static string FormatExchange(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange)) return "";
        var labels = new Dictionary<string, string> { ["p"] = "过去式", ["d"] = "过去分词", ["i"] = "现在分词", ["3"] = "第三人称单数", ["r"] = "比较级", ["t"] = "最高级", ["s"] = "复数", ["0"] = "原形" };
        return string.Join("    ", exchange.Split('/').Select(x => x.Split(':', 2)).Where(x => x.Length == 2 && labels.ContainsKey(x[0])).Select(x => $"{labels[x[0]]}  {x[1]}"));
    }
    private void UpdateHistory()
    {
        _changingLists = true;
        HistoryList.ItemsSource = _preferences.SearchHistory.Take(20).ToList();
        HistoryEmptyText.Visibility = _preferences.SearchHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Content = _preferences.SearchHistory.Count == 0 ? "全部记录" : $"全部 {_preferences.SearchHistory.Count} 条";
        _changingLists = false;
    }
    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        await UpdateSuggestionsAsync(SearchBox.Text);
    }
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; await SearchAsync(SearchBox.Text); }
        else if (e.Key == Key.Down && SuggestionList.Items.Count > 0) { SuggestionList.Focus(); e.Handled = true; SuggestionList.SelectedIndex = 0; }
    }
    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text);
    private async void SuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingLists) return;
        var word=SuggestionList.SelectedItem switch {string spelling=>spelling,ChineseSearchResult result=>result.Word,_=>null};
        if(word==null)return;
        SearchBox.Text = word;
        await SearchAsync(word);
    }
    private async void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingLists || HistoryList.SelectedItem is not SearchHistoryEntry item) return;
        var word = item.Word;
        SearchBox.Text = word;
        await SearchAsync(word);
    }
    private async void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HistoryWindow(this, _preferences);
        var selected = dialog.ShowDialog() == true ? dialog.SelectedWord : null;
        UpdateHistory();
        if (selected != null) { SearchBox.Text = selected; await SearchAsync(selected); }
    }

    private async Task RefreshNotepadsAsync(string? selectId = null)
    {
        if (!_allowCloud) { CloudDetailText.Text = "离线模式"; EditNotepadButton.IsEnabled=RefreshNotepadsButton.IsEnabled = CreateNotepadButton.IsEnabled = NotepadCombo.IsEnabled = false; return; }
        if (_refreshing) return;
        if (!_tokens.HasToken) { SetAccountStatus(InactiveAccountStatus); CloudDetailText.Text = "点击标题栏的账号按钮登录墨墨"; return; }
        _refreshing = true;
        var account=_accountVersion;var accountToken=_accountCts.Token;
        UpdateTodayStudyButton();
        _notepadCache.Clear();
        RefreshNotepadsButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        try
        {
            var pads = (await _api.ListNotepadsAsync(accountToken)).Where(pad=>!_deletedNotepads.Contains(pad.Id)).ToArray();
            if(account!=_accountVersion||accountToken.IsCancellationRequested)return;
            _changingLists = true;
            NotepadCombo.ItemsSource = pads;
            NotepadCombo.SelectedItem = pads.FirstOrDefault(x => x.Id == (selectId ?? _preferences.NotepadId)) ?? pads.FirstOrDefault();
            _changingLists = false;
            _preferences.NotepadId = (NotepadCombo.SelectedItem as CloudNotepad)?.Id;
            _preferences.Save();
            CloudDetailText.Text = pads.Length == 0 ? "还没有云词本，点击新建" : $"{pads.Length} 个云词本";
            SetAccountStatus("已连接");
        }
        catch (OperationCanceledException)when(account!=_accountVersion||accountToken.IsCancellationRequested){}
        catch (Exception ex) { if(account==_accountVersion){SetAccountError(ex);CloudDetailText.Text=UserError(ex);} }
        finally { if(account==_accountVersion){_changingLists = false; _refreshing = false; RefreshNotepadsButton.IsEnabled = !_adding; SettingsButton.IsEnabled = !_adding; await UpdateMembershipAsync(); UpdateTodayStudyButton();} }
    }
    private async void RefreshNotepadsButton_Click(object sender, RoutedEventArgs e) => await RefreshNotepadsAsync();
    private async void NotepadCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingLists) return;
        _preferences.NotepadId = (NotepadCombo.SelectedItem as CloudNotepad)?.Id;
        _preferences.Save();
        await UpdateMembershipAsync();
    }
    private async void AddButton_Click(object sender, RoutedEventArgs e) => await AddCurrentWordAsync();
    private async Task AddCurrentWordAsync()
    {
        if (!_allowCloud) return;
        if (_adding || !ValidWord(_word)) return;
        if (_refreshing) { StatusText.Text = "正在刷新云词本，请稍候。"; return; }
        if (!_tokens.HasToken) { AccountButton_Click(this, new RoutedEventArgs()); return; }
        if (NotepadCombo.SelectedItem is not CloudNotepad pad) { await CreateNotepadForCurrentWordAsync(); return; }
        var word = _word;
        if(_membershipWord!=word||_membershipPad!=pad.Id)
        {
            await UpdateMembershipAsync();
            if(_membershipWord!=word||_membershipPad!=pad.Id||word!=_word)return;
        }
        var remove=_isMember;
        _adding = true;
        SetCloudWriteBusy(true);
        AddButton.IsEnabled = false;
        StatusText.Text = remove?$"正在从「{pad.Title}」移除…":$"正在加入「{pad.Title}」…";
        try
        {
            var voc = _vocabulary?.Spelling.Equals(word, StringComparison.OrdinalIgnoreCase) == true ? _vocabulary : await _api.ResolveWordAsync(word);
            if (voc == null) throw new MaiMemoException("墨墨未收录这个单词，请检查拼写。");
            if(remove)
            {
                var result=await _api.RemoveFromNotepadAsync(pad.Id,voc.Spelling);ApplyNotepadSnapshot(result.Notepad);
                StatusText.Text=$"已从「{pad.Title}」移除「{word}」。";
            }
            else
            {
                var result=await _api.AddToNotepadAsync(pad.Id,voc.Spelling);ApplyNotepadSnapshot(result.Notepad);
                StatusText.Text=result.AlreadyExisted?$"「{word}」已在「{pad.Title}」中。":$"已将「{word}」加入「{pad.Title}」";
            }
        }
        catch(EmptyNotepadException error)
        {
            if(ConfirmNotepadDeletion(this,error.Notepad,1))
            {
                try{await _api.DeleteNotepadAsync(error.Notepad);RemoveNotepadSnapshot(error.Notepad.Id);}
                catch(Exception failure){ShowError(failure);}
            }
            else StatusText.Text="已取消删除云词本，原单词已保留。";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _adding = false; SetCloudWriteBusy(false); await UpdateMembershipAsync(); }
    }
    private async void CreateNotepadButton_Click(object sender, RoutedEventArgs e) => await CreateNotepadForCurrentWordAsync();
    private async Task CreateNotepadForCurrentWordAsync()
    {
        if (!_allowCloud) return;
        if (_adding) return;
        if (_refreshing) { StatusText.Text = "正在刷新云词本，请稍候。"; return; }
        if (!_tokens.HasToken) { AccountButton_Click(this, new RoutedEventArgs()); return; }
        var word = _word;
        if (!ValidWord(word)) { StatusText.Text = "请先查询一个英文单词，再新建云词本。"; return; }
        var title = InputDialog.Ask(this, "新建墨墨云词本", $"新词本将包含当前单词「{word}」。", "桌面生词本", "新建并加入");
        if (title == null) return;
        _adding = true;
        SetCloudWriteBusy(true);
        CreateNotepadButton.IsEnabled = false;
        AddButton.IsEnabled = false;
        try
        {
            var voc = await _api.ResolveWordAsync(word);
            if (voc == null) throw new MaiMemoException("墨墨未收录这个单词，请先查询其他单词。");
            var pad = await _api.CreateNotepadAsync(title, voc.Spelling);
            CacheNotepad(pad);
            _preferences.NotepadId = pad.Id;
            _preferences.Save();
            await RefreshNotepadsAsync(pad.Id);
            ApplyNotepadSnapshot(pad);
            StatusText.Text = $"已创建「{pad.Title}」，并加入「{word}」。";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _adding = false; SetCloudWriteBusy(false); await UpdateMembershipAsync(); }
    }
    private void AccountButton_Click(object sender, RoutedEventArgs e)=>EditSettings("墨墨账号");
    private void SettingsButton_Click(object sender, RoutedEventArgs e)=>EditSettings("常规");
    private void EditSettings(string? initialPage=null)
    {
        if (_adding || _refreshing) return;
        _editingSettings=true;ReleaseGlobalShortcut();
        try
        {
            SettingsDialog.Edit(this,_tokens,_preferences,ValidateGlobalShortcut,ReloadAccountAsync,
                initialPage??(_updates?.CanInstall==true?"软件更新":!_tokens.HasToken?"墨墨账号":null),_updates,
                startOnboarding:StartOnboarding,settingsChanged:ApplySettingsUI);
        }
        finally{_editingSettings=false;RefreshGlobalShortcut();RefreshDoubleCopy();RefreshHoverLookup();QueuePendingUpdateNotice();}
    }
    private void ApplySettingsUI()
    {
        UpdateShortcutHints();
        UpdateExamCountdown();
    }
    private void SetCloudWriteBusy(bool busy)
    {
        SettingsButton.IsEnabled = !busy;
        AccountButton.IsEnabled = !busy;
        NotepadCombo.IsEnabled = !busy;
        CreateNotepadButton.IsEnabled = !busy;
        RefreshNotepadsButton.IsEnabled = !busy;
        EditNotepadButton.IsEnabled=!busy&&NotepadCombo.SelectedItem is CloudNotepad;
        UpdateTodayStudyButton();
    }
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _preferences.AlwaysOnTop = Topmost;
        _preferences.Save();
        StatusText.Text = Topmost ? "窗口置顶已开启。" : "窗口置顶已关闭。";
        UpdatePinAppearance();
    }
    private void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidWord(_word)) return;
        try
        {
            if (_speaker == null)
            {
                _speaker = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!);
                dynamic voices = _speaker!.GetVoices("Language=409");
                if (voices.Count > 0) _speaker.Voice = voices.Item(0);
                else throw new InvalidOperationException();
            }
            _speaker.Speak(_word, 3);
            StatusText.Text = "正在播放单词发音。";
        }
        catch { StatusText.Text = "未找到可用的 Windows 英语语音，可在系统语言设置中安装英语语音包。"; }
    }
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if(_onboardingTour?.HandleKey(e)==true)return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if(key is Key.BrowserBack or Key.BrowserForward){e.Handled=true;await MoveNavigationAsync(key==Key.BrowserBack?-1:1);return;}
        if(ShortcutGestures.Match(_preferences,key,Keyboard.Modifiers) is { } command){e.Handled=true;await ExecuteShortcutAsync(command);}
    }
    private void InstallHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(HotkeyHook);
        RefreshGlobalShortcut();
    }
    private IntPtr HotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if(App.MaintenanceExitMessage!=0&&message==App.MaintenanceExitMessage)
        {
            handled=true;Dispatcher.BeginInvoke(new Action(ExitApplication));return IntPtr.Zero;
        }
        if (message == 0x0024 && SetMaximizedBounds(hwnd, lParam)) { handled = true; return IntPtr.Zero; }
        if (App.ActivateMessage != 0 && message == App.ActivateMessage)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(RestoreWindow));
            return IntPtr.Zero;
        }
        if (message == 0x0312 && GlobalShortcuts.FirstOrDefault(item=>item.Id==wParam.ToInt32()).Command is { } command)
        {
            handled = true;
            if(_editingSettings||_screenTranslating)return IntPtr.Zero;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if(command=="screenshot"){await StartScreenshotTranslationAsync();return;}
                RestoreWindow();
                if(OwnedWindows.Cast<Window>().LastOrDefault(window=>window.IsVisible) is { } dialog)
                {
                    dialog.Activate();
                    if(_desktopIntegration)new ForegroundWindow().Activate(new WindowInteropHelper(dialog).Handle,dialog.Topmost);
                    return;
                }
                if(command=="search")return;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        var text = Clipboard.GetText().Trim();
                        await ProcessClipboardTextAsync(text);
                    }
                }
                catch (ExternalException) { StatusText.Text = "暂时无法读取剪贴板，请在搜索框输入单词。"; }
                SearchBox.Focus(); SearchBox.SelectAll();
            }));
        }
        return IntPtr.Zero;
    }
    private static string UserError(Exception ex) => ex is MaiMemoException or ArgumentException ? ex.Message : ex is OperationCanceledException ? "请求已取消，请重试。" : "网络暂时不可用，请稍后重试。";
    private void ShowError(Exception ex) => StatusText.Text = UserError(ex);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
