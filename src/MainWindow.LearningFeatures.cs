using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MemoLex.Core;
using MemoLex.Desktop;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private sealed record PendingWordContext(string Word, string? MatchedForm, string Sentence, string Translation, string Source);

    private readonly ContextSentenceStore _contextSentences = new();
    private readonly SpellingMistakeStore _spellingMistakes = new();
    private PendingWordContext? _pendingContext;
    private bool _recallModeActive;
    private DispatcherTimer? _recallTimer;
    private DateTime _recallStartedAt;

    private void EnterRecallMode(string word)
    {
        StopRecallMode();
        _recallModeActive = true;
        var count = _preferences.SearchHistory
            .FirstOrDefault(h => h.Word.Equals(word, StringComparison.OrdinalIgnoreCase))?.LookupCount ?? 1;
        RecallPromptTitle.Text = count >= 2
            ? $"这是你第 {count} 次查「{word}」，先回想 2 秒？"
            : $"先别急着看释义，回想 2 秒「{word}」的意思？";
        RecallPromptSubtitle.Text = "在脑海里快速回忆它的含义或常见用法，随时可以按空格 / 回车或点击下方按钮揭晓。";
        RecallCountdownText.Text = "回想中 · 2s";
        RecallProgressFill.Width = 0;

        RecallMaskCard.Visibility = Visibility.Visible;
        DefinitionRevealBody.Visibility = Visibility.Collapsed;
        NotesDivider.Visibility = Visibility.Collapsed;
        NotesSection.Visibility = Visibility.Collapsed;
        SavedContextDivider.Visibility = Visibility.Collapsed;
        SavedContextSection.Visibility = Visibility.Collapsed;
        ExamplesDivider.Visibility = Visibility.Collapsed;
        ExamplesSection.Visibility = Visibility.Collapsed;
        RepeatedWordBanner.Visibility = Visibility.Collapsed;

        _recallStartedAt = DateTime.UtcNow;
        _recallTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _recallTimer.Tick += (_, _) =>
        {
            if (!_recallModeActive) { _recallTimer?.Stop(); return; }
            var elapsed = (DateTime.UtcNow - _recallStartedAt).TotalMilliseconds;
            var ratio = Math.Clamp(elapsed / 2000.0, 0, 1);
            if (RecallProgressTrack.ActualWidth > 0)
                RecallProgressFill.Width = RecallProgressTrack.ActualWidth * ratio;
            if (ratio >= 1.0)
            {
                RecallCountdownText.Text = "回想完成 · 可揭晓";
                _recallTimer?.Stop();
            }
            else
            {
                var remainSec = Math.Max(1, (int)Math.Ceiling((2000.0 - elapsed) / 1000.0));
                RecallCountdownText.Text = $"回想中 · {remainSec}s";
            }
        };
        _recallTimer.Start();
        UiMotion.Reveal(RecallMaskCard, distance: 4);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_recallModeActive) RecallRevealButton.Focus();
        }));
    }

    private void StopRecallMode()
    {
        if (_recallTimer != null)
        {
            _recallTimer.Stop();
            _recallTimer = null;
        }
        var wasActive = _recallModeActive;
        _recallModeActive = false;
        if (RecallMaskCard != null) RecallMaskCard.Visibility = Visibility.Collapsed;
        if (DefinitionRevealBody != null) DefinitionRevealBody.Visibility = Visibility.Visible;
        if (NotesDivider != null) NotesDivider.Visibility = Visibility.Visible;
        if (NotesSection != null) NotesSection.Visibility = Visibility.Visible;
        if (ExamplesDivider != null) ExamplesDivider.Visibility = Visibility.Visible;
        if (ExamplesSection != null) ExamplesSection.Visibility = Visibility.Visible;
        if (wasActive && ValidWord(_word))
        {
            UpdateSavedContextVisibility(_word);
            UpdateRepeatedWordBanner();
        }
    }

    private void RevealFromRecallMode()
    {
        if (!_recallModeActive) return;
        StopRecallMode();
        UiMotion.Reveal(DefinitionRevealBody, distance: 4);
        StatusText.Text = "已揭晓释义。";
    }

    private void RecallRevealButton_Click(object sender, RoutedEventArgs e) => RevealFromRecallMode();
    private void RepeatedRecallButton_Click(object sender, RoutedEventArgs e)
    {
        if (ValidWord(_word)) EnterRecallMode(_word);
    }

    private void SyncRecallModeToggle()
    {
        if (RecallModeToggle == null) return;
        RecallModeToggle.IsChecked = _preferences.RecallModeEnabled;
        RecallModeToggle.ToolTip = _preferences.RecallModeEnabled
            ? "先回想模式（已开启）：查词时会先遮住释义 2 秒，点击可关闭"
            : "先回想模式（已关闭）：开启后，查词时会先遮住释义 2 秒，帮你主动回忆";
    }

    private void RecallModeToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = RecallModeToggle.IsChecked == true;
        _preferences.RecallModeEnabled = enabled;
        _preferences.Save();
        SyncRecallModeToggle();
        if (!enabled && _recallModeActive)
            StopRecallMode();
        StatusText.Text = enabled
            ? "已开启「先回想」模式，后续查词将先遮住释义 2 秒。"
            : "已关闭「先回想」模式。";
    }

    private void UpdateContextCaptureBanner(string word)
    {
        if (ContextCaptureBanner == null) return;
        if (_pendingContext is { } ctx && ctx.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ctx.Sentence))
        {
            ContextCaptureSourceText.Text = $"来自刚读过的「{ctx.Source}」原句：";
            ContextCaptureSentenceText.Text = ctx.Sentence;
            var alreadySaved = _contextSentences.Contains(ctx.Word, ctx.Sentence);
            SavePendingContextButton.Content = alreadySaved ? "✓ 已保存 · 原句自测" : "＋ 保存这句，稍后考我";
            ContextCaptureBanner.Visibility = Visibility.Visible;
        }
        else
        {
            ContextCaptureBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void SavePendingContextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingContext is not { } ctx) return;
        if (_contextSentences.Contains(ctx.Word, ctx.Sentence))
        {
            OpenContextSentenceReview(ctx.Word);
            return;
        }
        _contextSentences.Save(ctx.Word, ctx.Sentence, ctx.Translation, ctx.Source, ctx.MatchedForm);
        SavePendingContextButton.Content = "✓ 已保存 · 原句自测";
        RenderSavedContexts(_word);
        UpdateContextReviewButtons();
        StatusText.Text = $"已保存「{ctx.Word}」的原句，离线也能随时自测。";
    }

    private void UpdateSavedContextVisibility(string word)
    {
        var hasItems = !_recallModeActive && ValidWord(word) && _contextSentences.List(word).Count > 0;
        if (SavedContextDivider != null) SavedContextDivider.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        if (SavedContextSection != null) SavedContextSection.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderSavedContexts(string word)
    {
        if (SavedContextPanel == null) return;
        SavedContextPanel.Children.Clear();
        if (!ValidWord(word))
        {
            UpdateSavedContextVisibility("");
            return;
        }
        var items = _contextSentences.List(word);
        if (items.Count == 0)
        {
            UpdateSavedContextVisibility(word);
            return;
        }

        SavedContextHintText.Text = $"当前单词已保存 {items.Count} 条原句 · 全部共 {_contextSentences.Count} 条，离线也能原句自测";
        var pillStyle = TryFindResource("PillButton") as Style;
        foreach (var item in items)
        {
            var captured = item;
            var card = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(actions, Dock.Right);

            var speakBtn = new Button
            {
                Content = "朗读",
                FontSize = 11,
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "朗读这条原句"
            };
            if (pillStyle != null) speakBtn.Style = pillStyle;
            speakBtn.Click += async (_, _) => await SpeakExampleAsync(speakBtn, captured.Sentence);

            var removeBtn = new Button
            {
                Content = "删除",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "删除这条原句"
            };
            removeBtn.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
            removeBtn.Click += (_, _) =>
            {
                _contextSentences.Remove(captured.Id);
                RenderSavedContexts(_word);
                UpdateContextCaptureBanner(_word);
                UpdateContextReviewButtons();
                StatusText.Text = "已删除该条原句。";
            };
            actions.Children.Add(speakBtn);
            actions.Children.Add(removeBtn);
            head.Children.Add(actions);

            var meta = captured.ReviewCount > 0
                ? $"{captured.Source} · 已自测 {captured.ReviewCount} 次"
                : captured.Source;
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = meta,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            };
            badge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
            badge.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            head.Children.Add(badge);
            card.Children.Add(head);

            var matchSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ExampleText.NormalizeLookup(word) };
            if (!string.IsNullOrWhiteSpace(captured.MatchedForm))
                matchSet.Add(ExampleText.NormalizeLookup(captured.MatchedForm));
            card.Children.Add(BuildSentenceText(captured.Sentence, matchSet, word, 15));

            if (!string.IsNullOrWhiteSpace(captured.Translation))
            {
                var zh = new TextBlock
                {
                    Text = captured.Translation,
                    FontSize = 13,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 7, 0, 0)
                };
                zh.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                card.Children.Add(zh);
            }

            var border = new Border
            {
                Child = card,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(15, 12, 15, 13),
                Margin = new Thickness(0, 0, 0, 9)
            };
            border.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            SavedContextPanel.Children.Add(border);
        }
        UpdateSavedContextVisibility(word);
    }

    private void UpdateContextReviewButtons()
    {
        var count = _contextSentences.Count;
        if (TranslationContextReviewButton != null)
            TranslationContextReviewButton.Content = count > 0 ? $"原句复习 ({count})" : "原句复习";
        if (WordContextReviewButton != null)
            WordContextReviewButton.Content = count > 0 ? $"原句自测 ({count})" : "原句自测";
    }

    private void ContextReviewButton_Click(object sender, RoutedEventArgs e) => OpenContextSentenceReview();
    private void WordContextReviewButton_Click(object sender, RoutedEventArgs e) => OpenContextSentenceReview(_word);

    private void OpenContextSentenceReview(string? focusWord = null)
    {
        var all = _contextSentences.List();
        if (all.Count == 0)
        {
            StatusText.Text = "还没有保存的原句。在段落翻译、截图翻译或真题例句中点击「保存原句」即可积累。";
            return;
        }

        var orderedItems = all.ToList();
        if (!string.IsNullOrWhiteSpace(focusWord))
        {
            orderedItems = all
                .OrderByDescending(item => item.Word.Equals(focusWord, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        var uniqueWords = orderedItems
            .Select(item => item.Word.Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dialog = StubbornFlashcards.Build(
            this,
            uniqueWords,
            _dictionary,
            _onlineDictionary,
            word => _mnemonics.Get(word, MnemonicAccountKey).FirstOrDefault()?.Text,
            persistProgress: false,
            title: "我的原句 · 离线自测",
            speak: (w, accent) => PlaySpeechAsync(w, accent),
            getMnemonics: w => _mnemonics.Get(w, MnemonicAccountKey),
            mnemonicStore: _mnemonics,
            preferences: _preferences,
            spellingMistakes: _spellingMistakes,
            contextSentences: _contextSentences,
            contextDeck: orderedItems);
        dialog.ShowDialog();
        UpdateContextReviewButtons();
        if (ValidWord(_word)) RenderSavedContexts(_word);
    }

    private CacheMaintenanceHost BuildCacheMaintenance()
    {
        var profile = _tokens.ProfileDirectory;
        var updatesDir = Path.Combine(profile, "updates");
        var browserDir = Path.Combine(profile, "dictionary-browser");

        IReadOnlyList<CacheCategoryItem> Load()
        {
            var historyCount = _preferences.SearchHistory.Count;
            var contextCount = _contextSentences.Count;
            var contextBytes = _contextSentences.FileSizeBytes;
            var spellingCount = _spellingMistakes.ActiveCount;
            var spellingBytes = _spellingMistakes.FileSizeBytes;
            var (flashcardCount, flashcardBytes) = FlashcardProgressStore.MeasureAll(profile);
            var (stubbornCount, stubbornBytes) = StubbornWordCache.MeasureAll(profile);
            var memoryCount = _cloudCache.Count + _examples.CachedCount + _onlineDictionary.CachedCount + _translation.CachedCount + _pronunciation.CachedCount;
            var tempDiskBytes = CacheMaintenanceHost.MeasureDirectoryBytes(updatesDir) + CacheMaintenanceHost.MeasureDirectoryBytes(browserDir);
            var networkTotalBytes = _pronunciation.CachedBytes + tempDiskBytes;
            var hasNetwork = memoryCount > 0 || tempDiskBytes > 0;
            var networkSummary = !hasNetwork
                ? "暂无缓存"
                : memoryCount > 0 && networkTotalBytes > 0
                    ? $"{memoryCount} 项内存缓存 · {CacheMaintenanceHost.FormatBytes(networkTotalBytes)}"
                    : memoryCount > 0
                        ? $"{memoryCount} 项内存缓存"
                        : CacheMaintenanceHost.FormatBytes(networkTotalBytes);

            return
            [
                new CacheCategoryItem(
                    "history",
                    "查词历史与次数",
                    "本机查词记录及「查过 N 次」计数，清除后「最近查词」与建议角标会重置。",
                    historyCount > 0 ? $"{historyCount} 条记录" : "暂无记录",
                    historyCount > 0,
                    "将清空本机的全部查词历史与查词次数统计，不会影响云词本和账号登录。"),
                new CacheCategoryItem(
                    "contexts",
                    "已保存的原句",
                    "从段落翻译、截图翻译与真题例句保存的离线原句及复习计数。",
                    contextCount > 0 ? $"{contextCount} 条原句 · {CacheMaintenanceHost.FormatBytes(contextBytes)}" : "暂无数据",
                    contextCount > 0,
                    "将清空所有保存在本机的阅读原句，离线原句自测列表将变为空。"),
                new CacheCategoryItem(
                    "spelling",
                    "拼写错点记录",
                    "卡片默写中记住的具体易错点与 2 选 1 错点微题记录。",
                    spellingCount > 0 ? $"{spellingCount} 个易错词 · {CacheMaintenanceHost.FormatBytes(spellingBytes)}" : "暂无记录",
                    spellingCount > 0,
                    "将清空已记录的拼写易错点，后续拼写练习会重新按实际输入记录。"),
                new CacheCategoryItem(
                    "flashcards",
                    "卡片自测进度",
                    "顽固单词与各个云词本的本地卡片自测续接进度。",
                    flashcardCount > 0 ? $"{flashcardCount} 份进度 · {CacheMaintenanceHost.FormatBytes(flashcardBytes)}" : "暂无进度",
                    flashcardCount > 0,
                    "将清除所有本地保存的卡片自测进度，下次打开自测将从第 1 组重新开始。"),
                new CacheCategoryItem(
                    "stubborn",
                    "顽固单词本地缓存",
                    "用于离线浏览顽固单词列表的本机快照，清除后联网可重新同步。",
                    stubbornCount > 0 ? $"{stubbornCount} 份缓存 · {CacheMaintenanceHost.FormatBytes(stubbornBytes)}" : "暂无缓存",
                    stubbornCount > 0,
                    "将清除本机缓存的顽固单词快照，不会退出墨墨账号，联网后会自动重新同步。"),
                new CacheCategoryItem(
                    "network",
                    "网络与临时缓存",
                    "真题例句、联网释义、段落翻译、朗读语音内存缓存，以及更新包和海词浏览临时文件。",
                    networkSummary,
                    hasNetwork,
                    "将清除联网查询、翻译与语音缓存及本机临时文件，下次访问时重新获取。")
            ];
        }

        string ClearOne(string id)
        {
            switch (id)
            {
                case "history":
                    _preferences.ClearHistory();
                    _preferences.Save();
                    UpdateHistory();
                    _ = UpdateSuggestionsAsync(SearchBox.Text);
                    UpdateRepeatedWordBanner();
                    return "已清除查词历史与查词次数。";
                case "contexts":
                    _contextSentences.Clear();
                    UpdateContextReviewButtons();
                    if (ValidWord(_word))
                    {
                        RenderSavedContexts(_word);
                        UpdateContextCaptureBanner(_word);
                    }
                    return "已清除所有已保存的原句。";
                case "spelling":
                    _spellingMistakes.Clear();
                    return "已清除拼写错点记录。";
                case "flashcards":
                    FlashcardProgressStore.RemoveAll(profile);
                    return "已清除所有卡片自测进度。";
                case "stubborn":
                    StubbornWordCache.RemoveAll(profile);
                    ResetStubbornWords();
                    return "已清除顽固单词本地缓存。";
                case "network":
                    _cloudCache.Clear();
                    _examples.ClearCache();
                    _onlineDictionary.ClearCache();
                    _translation.ClearCache();
                    _pronunciation.ClearCache();
                    CacheMaintenanceHost.ClearDirectoryBestEffort(updatesDir);
                    CacheMaintenanceHost.ClearDirectoryBestEffort(browserDir);
                    return "已清除网络与临时缓存。";
                default:
                    return "";
            }
        }

        string ClearAll()
        {
            var items = Load().Where(c => c.HasData).ToArray();
            if (items.Length == 0) return "当前没有可清除的缓存。";
            foreach (var item in items) ClearOne(item.Id);
            return $"已清除全部 {items.Length} 类本机缓存与记录。";
        }

        return new CacheMaintenanceHost(Load, ClearOne, ClearAll);
    }
}
