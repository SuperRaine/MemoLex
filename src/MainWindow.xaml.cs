using System.IO;
using System.ComponentModel;
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
using MemoLex.Desktop;
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
    private TrayIcon? _tray;
    private QuickLookupBar? _quickLookupBar;
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
        ThemeManager.Apply(_preferences.ThemeMode);
        InitializeComponent();
        UpdateThemeToggleIcon();
        SyncRecallModeToggle();
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
            ThemeManager.ApplyWindowFrame(this);
            InstallHotkey();
            _source?.AddHook(ResizeHook);
            if (!desktopIntegration) return;
            RefreshDoubleCopy();
            EnsureTrayIcon();
        };
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) {_restoreState = WindowState;QueuePendingUpdateNotice();} UpdateWindowChrome(); };
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += Window_PreviewMouseDown;
        Closed += (_, _) =>
        {
            EndResizeSurface();
            _source?.RemoveHook(ResizeHook);
            StopOnboardingOnExit();
            _examCountdownTimer?.Stop();
            StopRecallMode();
            _doubleCopy?.Dispose();_clipboardCts?.Cancel();
            _hover?.Dispose();_accountTimer?.Stop();_accountWatcher?.Dispose();_updates?.Dispose();
            _studyCts?.Cancel();
            _stubbornCts?.Cancel();_stubbornDialog?.Close();
            _quickLookupBar?.Close();
            _studyLoadCts?.Cancel();_accountCts.Cancel();_api.AuthenticationResult-=OnApiAuthenticationResult;
            _studyDayTimer?.Stop();
            _lookupCts?.Cancel();
            StopPronunciation();
            _pronunciation.Dispose();
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
        if (e.Cancel || _exitRequested || !_desktopIntegration) return;
        var closeToTray=ResolveCloseAction();
        if(closeToTray==null){e.Cancel=true;return;}
        if(!closeToTray.Value)return;
        e.Cancel = true;
        _preferences.Save();
        if (EnsureTrayIcon())
        {
            Hide();
            _tray!.ShowHiddenHint(ShortcutLabel("clipboard"));
        }
        else
        {
            StatusText.Text = "托盘暂不可用，窗口已最小化。";
            WindowState = WindowState.Minimized;
        }
    }
    private bool EnsureTrayIcon()
    {
        if (_tray != null) return true;
        try
        {
            _tray = new TrayIcon(RestoreWindow, () => { RestoreWindow(); EditSettings("常规"); }, ExitApplication, OpenQuickLookupBar);
            return true;
        }
        catch { return false; }
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
            DictionaryCountText.Text=$"内置词库 · {_dictionary.Count:N0} 词";
            UpdateHistory();
            UpdateContextReviewButtons();
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
    private async Task LoadWordAsync(string raw, bool recordHistory = true, bool addNavigationEntry = true, ViewState? restore = null, bool recallMode = false, bool keepPendingContext = false)
    {
        if (!_ready) return;
        var word = ExampleText.NormalizeLookup(raw).Trim();
        if (!ValidWord(word)) { StatusText.Text = "请输入英文单词或短语（最多 100 字符）。"; return; }
        StopPronunciation();
        if (!keepPendingContext) _pendingContext = null;
        StopRecallMode();
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
        SpeakBritishButton.IsEnabled = SpeakAmericanButton.IsEnabled = true;
        HaiciButton.IsEnabled=true;
        WordText.ContextMenu=HaiciWordMenu(word);
        var entry = _dictionary.Lookup(word);
        var localMeaning = entry?.DisplayMeaning ?? "";
        var hasLocalMeaning = localMeaning.Length > 0;
        var lookOnline = !hasLocalMeaning && _allowCloud;
        WordText.Text = entry?.word ?? word;
        PhoneticText.Text = string.IsNullOrWhiteSpace(entry?.phonetic) ? "" : $"/{entry.phonetic.Trim('/')}/";
        FillWordLinks(DefinitionText, hasLocalMeaning ? localMeaning : lookOnline ? "正在查找释义…" : "暂无释义。",
            EmptyMatches, word, skipPartOfSpeech: true);
        RenderExchange(entry?.exchange, word);
        WordMetaText.Text = hasLocalMeaning ? "内置词库" : "";
        WordMetaText.ToolTip = hasLocalMeaning && int.TryParse(entry?.bnc, out var frequency) && frequency > 0
            ? $"BNC 词频排名 {frequency:N0}"
            : null;
        RenderTags(entry);
        RenderSpellingSuggestions(word, !hasLocalMeaning);
        ResultScroll.ScrollToTop();
        UiMotion.Reveal(DefinitionSection);
        ResetExampleView();
        if (recordHistory)
        {
            _preferences.RecordLookup(entry?.word ?? word);
            _preferences.Save();
            UpdateHistory();
        }
        UpdateContextCaptureBanner(entry?.word ?? word);
        RenderSavedContexts(entry?.word ?? word);
        var shouldRecall = recallMode || (_preferences.RecallModeEnabled && recordHistory && restore == null && hasLocalMeaning);
        if (shouldRecall) EnterRecallMode(entry?.word ?? word);
        else UpdateRepeatedWordBanner();
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

        FillWordLinks(DefinitionText, definition?.Meaning ?? "暂无释义。可在海词词典中查看。",
            EmptyMatches, word, skipPartOfSpeech: true);
        StatusText.Text = definition == null ? "暂无释义。" : "查词完成。";
        if (definition == null) return;

        UiMotion.Reveal(DefinitionText, distance: 0);
        WordMetaText.Text = definition.SourceLabel;
        WordMetaText.ToolTip = definition.SourceAttribution;
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
            ExamplesHintText.Text = "查看真题例句需要联网。";
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
            ExamplesHintText.Text = _shownExamples.Count == 0 ? "暂无例句。可切换考试类型。" : $"{_shownExamples.Count} / {page.Total} 条例句";
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
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var speakContent = new StackPanel { Orientation = Orientation.Horizontal };
        speakContent.Children.Add(new TextBlock { Text = "\uE767", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        speakContent.Children.Add(new TextBlock { Text = "朗读", VerticalAlignment = VerticalAlignment.Center });
        var speak = new Button
        {
            Content = speakContent, Style = (Style)FindResource("PillButton"),
            ToolTip = "朗读整条例句", FontSize = 11,
            MinWidth = 62, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0)
        };
        speak.Click += async (_, _) => await SpeakExampleAsync(speak, sentence.English);
        var alreadySavedSentence = _contextSentences.Contains(sourceWord, sentence.English);
        var saveSentenceBtn = new Button
        {
            Content = alreadySavedSentence ? "✓ 已保存原句" : "保存原句",
            Style = (Style)FindResource("PillButton"),
            ToolTip = "保存这条原句到本机，离线也能自测",
            FontSize = 11,
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(8, 0, 0, 0)
        };
        saveSentenceBtn.Click += (_, _) =>
        {
            if (_contextSentences.Contains(sourceWord, sentence.English))
            {
                OpenContextSentenceReview(sourceWord);
                return;
            }
            var srcLabel = string.IsNullOrWhiteSpace(sentence.Source) ? "真题例句" : sentence.Source;
            _contextSentences.Save(sourceWord, sentence.English, sentence.Translation, srcLabel, sentence.MatchedWords.FirstOrDefault());
            saveSentenceBtn.Content = "✓ 已保存原句";
            RenderSavedContexts(_word);
            UpdateContextReviewButtons();
            StatusText.Text = $"已保存「{sourceWord}」的原句，点击「原句自测」即可离线复习。";
        };
        var headingActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        headingActions.Children.Add(saveSentenceBtn);
        headingActions.Children.Add(speak);
        DockPanel.SetDock(headingActions, Dock.Right);
        heading.Children.Add(headingActions);
        var sourceBadge = new Border
        {
            Background = Brush("#E6F5FF"), BorderBrush = Brush("#CBE7FA"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2.5, 9, 2.5),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(sentence.Source) ? "真题例句" : sentence.Source,
                FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("#0A7FB3"), TextWrapping = TextWrapping.Wrap
            }
        };
        heading.Children.Add(sourceBadge);
        card.Children.Add(heading);
        if (sentence.IsOption)
        {
            card.Children.Add(new Border
            {
                Background = Brush("#FFF6E5"), BorderBrush = Brush("#F3D08A"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock { Text = $"原题选项 {sentence.OptionLabel}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("#B45309") }
            });
            if (sentence.ContextEnglish.Length > 0)
            {
                card.Children.Add(BuildSentenceText(sentence.ContextEnglish, matches, sourceWord, 14));
                card.Children.Add(new TextBlock { Text = "选项", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("#7D97AC"), Margin = new Thickness(0, 10, 0, 4) });
            }
        }
        card.Children.Add(BuildSentenceText(sentence.English, matches, sourceWord, 15.5));
        if (sentence.Translation.Length > 0)
            card.Children.Add(BuildExampleTranslation(sentence.Translation,sentence.English,sourceWord,matches,sentence.MatchedTranslations,sentence.TranslationAligned));
        if (sentence.IsOption && sentence.ContextTranslation.Length > 0)
            card.Children.Add(BuildExampleTranslation("原题译文："+sentence.ContextTranslation,sentence.ContextEnglish,sourceWord,matches,sentence.MatchedTranslations));
        var cardBorder = new Border
        {
            Child = card, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 15, 18, 16), Margin = new Thickness(0, 0, 0, 11)
        };
        cardBorder.SetResourceReference(Border.BackgroundProperty, "CardAltBrush");
        cardBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        ExamplesPanel.Children.Add(cardBorder);
        UiMotion.Reveal(cardBorder, distance: 5);
    }
    private static readonly IReadOnlySet<string> EmptyMatches = new HashSet<string>();
    private TextBlock BuildSentenceText(string text, IReadOnlySet<string> matches, string sourceWord, double fontSize = 16, bool accentLinks = false)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = fontSize, LineHeight = 27 };
        block.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        FillWordLinks(block, text, matches, sourceWord, accentLinks: accentLinks);
        return block;
    }
    private void FillWordLinks(TextBlock block, string text, IReadOnlySet<string> matches, string sourceWord, bool skipPartOfSpeech = false, Func<string,Task>? navigate = null, bool clear = true, bool accentLinks = false)
    {
        if(clear)block.Inlines.Clear();
        var parts = ExampleText.Tokenize(text);
        var dark = ThemeManager.IsDark;
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var isLabel = skipPartOfSpeech && part.LookupWord != null && index + 1 < parts.Count
                && parts[index + 1].Text.StartsWith('.') && DictionaryEntry.IsPartOfSpeechLabel(part.LookupWord);
            if (isLabel)
            {
                block.Inlines.Add(new Run(part.Text + ".")
                {
                    Foreground = Brush(dark ? "#5EC8FA" : "#0B84BD"),
                    FontWeight = FontWeights.Bold,
                    FontStyle = FontStyles.Italic
                });
                var rest = parts[index + 1].Text[1..];
                if (rest.Length > 0) block.Inlines.Add(new Run(rest));
                index++;
                continue;
            }
            if (part.LookupWord == null) { block.Inlines.Add(new Run(part.Text)); continue; }
            var word = part.LookupWord;
            if (accentLinks)
            {
                var prev = index > 0 ? parts[index - 1].Text : "";
                var next = index + 1 < parts.Count ? parts[index + 1].Text : "";
                if (!_dictionary.IsMnemonicLinkableWord(word, sourceWord, prev, next))
                {
                    var isSelf = !string.IsNullOrEmpty(sourceWord) && word.Equals(sourceWord, StringComparison.OrdinalIgnoreCase);
                    block.Inlines.Add(new Run(part.Text) { FontWeight = isSelf ? FontWeights.SemiBold : FontWeights.Normal });
                    continue;
                }
            }
            var highlighted = matches.Contains(word);
            var linkBrush = highlighted
                ? Brush(dark ? "#8BD9FF" : "#085680")
                : accentLinks
                    ? Brush(dark ? "#5EC8FA" : "#0A7FB3")
                    : Brush(dark ? "#E6F1F8" : "#183952");
            var link = new Hyperlink(new Run(part.Text)) { Foreground = linkBrush, TextDecorations = null,
                Background=highlighted?Brush(dark ? "#1B4A68" : "#D2EFFE"):Brushes.Transparent,
                FontWeight = (highlighted || accentLinks) ? FontWeights.SemiBold : FontWeights.Normal, ToolTip = "查询 " + word, Cursor = Cursors.Hand };
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
        var dark = ThemeManager.IsDark;
        var terms=TextHighlights.TranslationTerms(english,text,sourceWord,matches,Meaning,explicitTerms,aligned);
        var block=new TextBlock { TextWrapping=TextWrapping.Wrap,FontSize=13.5,LineHeight=23,Margin=new Thickness(0,9,0,0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        foreach(var part in TextHighlights.Split(text,terms))
            if(part.Highlighted)block.Inlines.Add(new Run(part.Text){Background=Brush(dark ? "#1B4A68" : "#D2EFFE"),Foreground=Brush(dark ? "#8BD9FF" : "#085680"),FontWeight=FontWeights.SemiBold});
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
        if (entry?.oxford == "1") AddTag("核心词汇", tone: "core");
        if (int.TryParse(entry?.collins, out var stars) && stars > 0) AddTag(new string('★', Math.Min(5, stars)), tone: "gold");
    }
    private void AddTag(string text, string? paperType = null, string tone = "sky")
    {
        var (bg, border, fg) = tone switch
        {
            "core" => ("#E7FAF2", "#A9E4CA", "#0E7C55"),
            "gold" => ("#FFF6E2", "#F4D489", "#B45309"),
            _ => ("#E8F6FF", "#C8E6FA", "#0A78AC")
        };
        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush(fg) };
        if (paperType == null)
        {
            TagsPanel.Children.Add(new Border
            {
                Background = Brush(bg), BorderBrush = Brush(border), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 5),
                Child = label
            });
        }
        else
        {
            var button = new Button
            {
                Content = label, Style = (Style)FindResource("PillButton"),
                Background = Brush(bg), BorderBrush = Brush(border),
                Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 5),
                ToolTip = $"查看{text}真题例句"
            };
            button.Click += async (_, _) => await OpenExamExamplesAsync(paperType);
            TagsPanel.Children.Add(button);
        }
    }
    private void RenderExchange(string? exchange, string sourceWord)
    {
        ExchangePanel.Children.Clear();
        if (string.IsNullOrWhiteSpace(exchange))
        {
            ExchangeCard.Visibility = Visibility.Collapsed;
            return;
        }
        var labels = new Dictionary<string, string> { ["p"] = "过去式", ["d"] = "过去分词", ["i"] = "现在分词", ["3"] = "第三人称单数", ["r"] = "比较级", ["t"] = "最高级", ["s"] = "复数", ["0"] = "原形" };
        var count = 0;
        foreach (var pair in exchange.Split('/').Select(x => x.Split(':', 2)).Where(x => x.Length == 2 && labels.ContainsKey(x[0])))
        {
            var formWord = pair[1].Trim();
            if (formWord.Length == 0) continue;
            var pill = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pill.Children.Add(new TextBlock { Text = labels[pair[0]], FontSize = 11, Foreground = Brush("#6E8DA4"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var formText = new TextBlock { FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var link = new Hyperlink(new Run(formWord)) { Foreground = Brush("#0A7FB3"), TextDecorations = null, ToolTip = "查询 " + formWord, Cursor = Cursors.Hand };
            link.ContextMenu = HaiciWordMenu(formWord);
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;
            link.Click += async (_, _) => await NavigateLinkedWordAsync(formWord, sourceWord);
            formText.Inlines.Add(link);
            pill.Children.Add(formText);
            ExchangePanel.Children.Add(new Border
            {
                Background = Brushes.White, BorderBrush = Brush("#D9EBF7"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 7, 5),
                Child = pill
            });
            count++;
        }
        ExchangeCard.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
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
        var word=SuggestionList.SelectedItem switch {WordSuggestionItem item=>item.Word,string spelling=>spelling,ChineseSearchResult result=>result.Word,_=>null};
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
        var account = _accountVersion;
        var token = _accountCts.Token;
        var key = (pad.Id, word.ToUpperInvariant());
        if (!_pendingNotepadWords.Add(key)) return;
        var membershipKnown = _membershipWord == word && _membershipPad == pad.Id;
        var remove = _isMember;
        var knownVocabulary = _vocabulary;
        bool CurrentAccount() => account == _accountVersion && !token.IsCancellationRequested;
        AddButton.Content = "正在保存…";
        AddButton.IsEnabled = false;
        try
        {
            if (!membershipKnown)
                remove = MaiMemoClient.ContainsWord(await ReadNotepadAsync(pad.Id), word);
            token.ThrowIfCancellationRequested();
            var voc = knownVocabulary?.Spelling.Equals(word, StringComparison.OrdinalIgnoreCase) == true
                ? knownVocabulary : await _api.ResolveWordAsync(word, token);
            if (voc == null) throw new MaiMemoException("墨墨未收录这个单词，请检查拼写。");
            if(remove)
            {
                var result = await _api.RemoveFromNotepadAsync(pad.Id, voc.Spelling, token);
                if (!CurrentAccount()) return;
                ApplyNotepadSnapshot(result.Notepad);
                StatusText.Text = $"已从「{pad.Title}」移除「{word}」";
            }
            else
            {
                var result = await _api.AddToNotepadAsync(pad.Id, voc.Spelling, token);
                if (!CurrentAccount()) return;
                ApplyNotepadSnapshot(result.Notepad);
                StatusText.Text = result.AlreadyExisted ? $"「{word}」已在「{pad.Title}」中" : $"已将「{word}」加入「{pad.Title}」";
            }
        }
        catch (OperationCanceledException) when (!CurrentAccount()) { }
        catch(EmptyNotepadException error)
        {
            if (!CurrentAccount()) return;
            if (ConfirmNotepadDeletion(this, error.Notepad, 1))
            {
                try
                {
                    await _api.DeleteNotepadAsync(error.Notepad, token);
                    if (CurrentAccount()) RemoveNotepadSnapshot(error.Notepad.Id);
                }
                catch (Exception failure) { if (CurrentAccount()) ShowError(failure); }
            }
            else StatusText.Text = "已取消删除云词本，原单词已保留";
        }
        catch (Exception ex) { if (CurrentAccount()) ShowError(ex); }
        finally
        {
            if (CurrentAccount())
            {
                _pendingNotepadWords.Remove(key);
                if ((NotepadCombo.SelectedItem as CloudNotepad)?.Id == pad.Id)
                    await UpdateMembershipAsync();
            }
        }
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
                startOnboarding:StartOnboarding,settingsChanged:ApplySettingsUI,cacheMaintenance:BuildCacheMaintenance());
        }
        finally{_editingSettings=false;RefreshGlobalShortcut();RefreshDoubleCopy();RefreshHoverLookup();QueuePendingUpdateNotice();}
    }
    private void ApplySettingsUI()
    {
        ThemeManager.Apply(_preferences.ThemeMode);
        UpdateThemeToggleIcon();
        SyncRecallModeToggle();
        if (!_preferences.RecallModeEnabled && _recallModeActive) StopRecallMode();
        UpdateShortcutHints();
        UpdateExamCountdown();
    }
    private void UpdateThemeToggleIcon()
    {
        if (ThemeToggleGlyph == null) return;
        ThemeToggleGlyph.Text = ThemeManager.IsDark ? "\uE706" : "\uE708";
        ThemeToggleButton.ToolTip = ThemeManager.IsDark ? "当前：深色模式（点击切换浅色）" : "当前：浅色模式（点击切换深色）";
    }
    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _preferences.ThemeMode = ThemeManager.IsDark ? "light" : "dark";
        _preferences.Save();
        ThemeManager.Apply(_preferences.ThemeMode);
        UpdateThemeToggleIcon();
        if (ValidWord(_word)) { RenderNotes(_word, _displayedNotes); RenderSavedContexts(_word); }
    }
    private void OpenQuickLookupBar()
    {
        if (!_ready) return;
        _quickLookupBar ??= new QuickLookupBar(
            _dictionary,
            _mnemonics,
            (w, accent) => PlaySpeechAsync(w, accent),
            async w =>
            {
                var target = HoverBookmarkStatus(w);
                if (!target.Available) return target.Title;
                if (target.Saved) return $"「{w}」已在「{target.Title}」中";
                return await SaveHoverWordAsync(w, target) ?? $"已加入「{target.Title}」";
            },
            w => { RestoreWindow(); _ = LoadWordAsync(w); },
            () => MnemonicAccountKey);
        _quickLookupBar.Popup();
    }
    private void DashboardButton_Click(object sender, RoutedEventArgs e)
    {
        var accountId = (_tokens.ActiveAccount ?? _tokens.ExpiredAccount)?.Id ?? "default";
        var progress = FlashcardProgressStore.Load(_tokens.ProfileDirectory, accountId);
        var dialog = LearningDashboardDialog.Build(
            this, _preferences, progress, _stubbornWords?.Total ?? 0,
            w => _ = LoadWordAsync(w),
            contextSentences: _contextSentences,
            spellingMistakes: _spellingMistakes,
            startContextReview: () => OpenContextSentenceReview());
        dialog.ShowDialog();
        UpdateContextReviewButtons();
        if (ValidWord(_word)) RenderSavedContexts(_word);
    }
    private void RenderSpellingSuggestions(string query, bool notFound)
    {
        SpellingSuggestionPanel.Children.Clear();
        if (!notFound)
        {
            SpellingSuggestionBanner.Visibility = Visibility.Collapsed;
            return;
        }
        var suggestions = _dictionary.SuggestSpellingCorrections(query, 5);
        if (suggestions.Count == 0)
        {
            SpellingSuggestionBanner.Visibility = Visibility.Collapsed;
            return;
        }
        var pillStyle = TryFindResource("PillButton") as Style;
        foreach (var item in suggestions)
        {
            var candidate = item;
            var btn = new Button
            {
                Content = candidate,
                FontSize = 12,
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 7, 4),
                ToolTip = $"点击查询「{candidate}」"
            };
            if (pillStyle != null) btn.Style = pillStyle;
            btn.Click += async (_, _) => await LoadWordAsync(candidate);
            SpellingSuggestionPanel.Children.Add(btn);
        }
        SpellingSuggestionBanner.Visibility = Visibility.Visible;
        UiMotion.Reveal(SpellingSuggestionBanner, distance: 4);
    }
    private void UpdateRepeatedWordBanner()
    {
        if (RepeatedWordBanner == null) return;
        if (_recallModeActive || _viewTarget != ViewTarget.Word || !ValidWord(_word))
        {
            RepeatedWordBanner.Visibility = Visibility.Collapsed;
            return;
        }
        var entry = _preferences.SearchHistory.FirstOrDefault(h => h.Word.Equals(_word, StringComparison.OrdinalIgnoreCase));
        var count = entry?.LookupCount ?? 0;
        var alreadyInPad = _membershipWord.Equals(_word, StringComparison.OrdinalIgnoreCase) && _isMember;
        if (count >= 3 && !alreadyInPad)
        {
            RepeatedWordText.Text = $"「{_word}」你已查询 {count} 次，疑似隐形顽固词，建议收藏到云词本巩固记忆。";
            RepeatedWordBanner.Visibility = Visibility.Visible;
        }
        else
        {
            RepeatedWordBanner.Visibility = Visibility.Collapsed;
        }
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
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if(_onboardingTour?.HandleKey(e)==true)return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if(_recallModeActive&&_viewTarget==ViewTarget.Word&&!SearchBox.IsKeyboardFocusWithin&&Keyboard.Modifiers==ModifierKeys.None&&key is Key.Space or Key.Enter)
        {
            e.Handled=true;
            if(!e.IsRepeat) RevealFromRecallMode();
            return;
        }
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
                if(command=="search"){OpenQuickLookupBar();return;}
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
