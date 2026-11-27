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
            if (_recallModeActive) RecallRememberedButton.Focus();
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

    private void RevealFromRecallMode(bool? remembered)
    {
        if (!_recallModeActive) return;
        StopRecallMode();
        UiMotion.Reveal(DefinitionRevealBody, distance: 4);
        StatusText.Text = remembered switch
        {
            true => "太棒了！核对一下释义是否和你想的一致。",
            false => "没关系，结合下方助记和例句再记一次，或加入云词本巩固。",
            _ => "已揭晓释义。"
        };
    }

    private void RecallRememberedButton_Click(object sender, RoutedEventArgs e) => RevealFromRecallMode(true);
    private void RecallForgotButton_Click(object sender, RoutedEventArgs e) => RevealFromRecallMode(false);
    private void RepeatedRecallButton_Click(object sender, RoutedEventArgs e)
    {
        if (ValidWord(_word)) EnterRecallMode(_word);
    }

    private bool TryResolveRecallTarget(out string word)
    {
        word = "";
        var raw = SearchBox.Text.Trim();
        if (raw.Length == 0 || ChineseReverseSearch.ContainsChinese(raw)) return false;
        if (SuggestionList.SelectedItem is WordSuggestionItem selected)
        {
            word = selected.Word;
            return ValidWord(word);
        }
        var normalized = ExampleText.NormalizeLookup(raw).Trim();
        if (ValidWord(normalized) && _dictionary.Lookup(normalized) != null)
        {
            word = normalized;
            return true;
        }
        if (SuggestionList.Items.Count > 0 && SuggestionList.Items[0] is WordSuggestionItem first)
        {
            word = first.Word;
            return ValidWord(word);
        }
        if (ValidWord(normalized))
        {
            word = normalized;
            return true;
        }
        return false;
    }

    private async void SuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None && SuggestionList.SelectedItem is WordSuggestionItem item)
        {
            e.Handled = true;
            SearchBox.Text = item.Word;
            await LoadWordAsync(item.Word, recallMode: true);
        }
    }

    private async void SuggestionRecallButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: string word } && ValidWord(word))
        {
            SearchBox.Text = word;
            await LoadWordAsync(word, recallMode: true);
        }
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
}
