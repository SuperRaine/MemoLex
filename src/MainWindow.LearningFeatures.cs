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
    private readonly List<(Button Button, string Word, string Sentence)> _exampleSaveButtons = [];
    private PendingWordContext? _pendingContext;

    private void RefreshExampleSaveButtons()
    {
        foreach (var (button, word, sentence) in _exampleSaveButtons)
        {
            var saved = _contextSentences.Contains(word, sentence);
            button.Content = saved ? "✓ 已收藏" : "收藏例句";
        }
    }
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
            ? $"第 {count} 次查「{word}」，先回想 2 秒"
            : $"先回想 2 秒「{word}」的意思";
        RecallPromptSubtitle.Text = "先在脑海里想一想它的释义或用法，随时按空格 / 回车查看答案。";
        RecallCountdownText.Text = "回想 2 秒";
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
                RecallCountdownText.Text = "可以查看了";
                _recallTimer?.Stop();
            }
            else
            {
                var remainSec = Math.Max(1, (int)Math.Ceiling((2000.0 - elapsed) / 1000.0));
                RecallCountdownText.Text = $"回想 {remainSec} 秒";
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
            ? "先回想模式（已开启）：再次查询已查过的单词时会先遮住释义 2 秒，点击可关闭"
            : "先回想模式（已关闭）：开启后，再次查询已查过的单词时会先遮住释义 2 秒，帮你主动回忆";
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
            ? "已开启「先回想」模式，再次查询已查过的单词将先遮住释义 2 秒。"
            : "已关闭「先回想」模式。";
    }

    private void UpdateContextCaptureBanner(string word)
    {
        if (ContextCaptureBanner == null) return;
        if (_pendingContext is { } ctx && ctx.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ctx.Sentence))
        {
            ContextCaptureSourceText.Text = $"来自「{ctx.Source}」";
            ContextCaptureSentenceText.Text = ctx.Sentence;
            var alreadySaved = _contextSentences.Contains(ctx.Word, ctx.Sentence);
            SavePendingContextButton.Content = alreadySaved ? "✓ 已收藏 · 去自测" : "＋ 收藏例句";
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
        SavePendingContextButton.Content = "✓ 已收藏 · 去自测";
        RenderSavedContexts(_word);
        RefreshExampleSaveButtons();
        UpdateContextReviewButtons();
        StatusText.Text = $"已收藏「{ctx.Word}」的例句";
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

        SavedContextHintText.Text = $"本词 {items.Count} 条 · 共 {_contextSentences.Count} 条";
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
                ToolTip = "朗读例句"
            };
            if (pillStyle != null) speakBtn.Style = pillStyle;
            speakBtn.Click += async (_, _) => await SpeakExampleAsync(speakBtn, captured.Sentence);

            var removeBtn = new Button
            {
                Content = "移除",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "移除这条收藏例句"
            };
            removeBtn.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
            removeBtn.Click += (_, _) =>
            {
                _contextSentences.Remove(captured.Id);
                RenderSavedContexts(_word);
                RefreshExampleSaveButtons();
                UpdateContextCaptureBanner(_word);
                UpdateContextReviewButtons();
                StatusText.Text = "已移除例句";
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
            TranslationContextReviewButton.Content = count > 0 ? $"例句自测 ({count})" : "例句自测";
        if (WordContextReviewButton != null)
            WordContextReviewButton.Content = count > 0 ? $"例句自测 ({count})" : "例句自测";
    }

    private void ContextReviewButton_Click(object sender, RoutedEventArgs e) => OpenContextSentenceReview();
    private void WordContextReviewButton_Click(object sender, RoutedEventArgs e) => OpenContextSentenceReview(_word);

    private void OpenContextSentenceReview(string? focusWord = null)
    {
        var all = _contextSentences.List();
        if (all.Count == 0)
        {
            StatusText.Text = "还没有收藏例句，可在翻译或真题例句中点击「收藏例句」";
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
            title: "我的例句 · 卡片自测",
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
                    ? $"{memoryCount} 项缓存 · {CacheMaintenanceHost.FormatBytes(networkTotalBytes)}"
                    : memoryCount > 0
                        ? $"{memoryCount} 项缓存"
                        : CacheMaintenanceHost.FormatBytes(networkTotalBytes);

            return
            [
                new CacheCategoryItem(
                    "history",
                    "查词历史与次数",
                    "本机查词记录及查词次数，清除后「最近查词」和重复次数标记会重置。",
                    historyCount > 0 ? $"{historyCount} 条记录" : "暂无记录",
                    historyCount > 0,
                    "将清空本机的全部查词历史与次数统计，不影响云词本和账号登录。"),
                new CacheCategoryItem(
                    "contexts",
                    "收藏的例句",
                    "在翻译和真题例句中收藏的例句及自测次数。",
                    contextCount > 0 ? $"{contextCount} 条例句 · {CacheMaintenanceHost.FormatBytes(contextBytes)}" : "暂无数据",
                    contextCount > 0,
                    "将清空本机收藏的全部例句，例句自测列表将变为空。"),
                new CacheCategoryItem(
                    "spelling",
                    "拼写易错记录",
                    "拼写默写中记住的易错位置与针对练习记录。",
                    spellingCount > 0 ? $"{spellingCount} 个单词 · {CacheMaintenanceHost.FormatBytes(spellingBytes)}" : "暂无记录",
                    spellingCount > 0,
                    "将清空已记录的拼写易错点，后续拼写练习会重新记录。"),
                new CacheCategoryItem(
                    "flashcards",
                    "卡片自测进度",
                    "顽固单词与云词本的卡片自测进度。",
                    flashcardCount > 0 ? $"{flashcardCount} 份进度 · {CacheMaintenanceHost.FormatBytes(flashcardBytes)}" : "暂无进度",
                    flashcardCount > 0,
                    "将清除所有本地卡片自测进度，下次打开将从第 1 组开始。"),
                new CacheCategoryItem(
                    "stubborn",
                    "顽固单词缓存",
                    "保存在本机的顽固单词列表，清除后联网会自动重新同步。",
                    stubbornCount > 0 ? $"{stubbornCount} 份缓存 · {CacheMaintenanceHost.FormatBytes(stubbornBytes)}" : "暂无缓存",
                    stubbornCount > 0,
                    "将清除本机缓存的顽固单词列表，不会退出账号，联网后会自动重新同步。"),
                new CacheCategoryItem(
                    "network",
                    "网络与临时缓存",
                    "真题例句、在线释义、翻译、语音缓存及临时文件。",
                    networkSummary,
                    hasNetwork,
                    "将清除在线查询、翻译与语音缓存及临时文件，下次使用时重新获取。")
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
                    return "已清除查词历史与次数。";
                case "contexts":
                    _contextSentences.Clear();
                    UpdateContextReviewButtons();
                    if (ValidWord(_word))
                    {
                        RenderSavedContexts(_word);
                        RefreshExampleSaveButtons();
                        UpdateContextCaptureBanner(_word);
                    }
                    return "已清除收藏的例句。";
                case "spelling":
                    _spellingMistakes.Clear();
                    return "已清除拼写易错记录。";
                case "flashcards":
                    FlashcardProgressStore.RemoveAll(profile);
                    return "已清除所有卡片自测进度。";
                case "stubborn":
                    StubbornWordCache.RemoveAll(profile);
                    ResetStubbornWords();
                    return "已清除顽固单词缓存。";
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
