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
    private readonly MistakeBookStore _mistakeBook = new();
    private readonly List<(Button Button, string Word, string Sentence)> _exampleSaveButtons = [];
    private PendingWordContext? _pendingContext;

    private void RefreshExampleSaveButtons()
    {
        var accountKey = MnemonicAccountKey;
        foreach (var (button, word, sentence) in _exampleSaveButtons)
        {
            var saved = _contextSentences.Contains(word, sentence, accountKey);
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
            var alreadySaved = _contextSentences.Contains(ctx.Word, ctx.Sentence, MnemonicAccountKey);
            SavePendingContextButton.Content = alreadySaved ? "✓ 已收藏" : "＋ 收藏例句";
            SavePendingContextButton.IsEnabled = !alreadySaved;
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
        if (_contextSentences.Contains(ctx.Word, ctx.Sentence, MnemonicAccountKey))
        {
            SavePendingContextButton.Content = "✓ 已收藏";
            SavePendingContextButton.IsEnabled = false;
            return;
        }
        _contextSentences.Save(ctx.Word, ctx.Sentence, ctx.Translation, ctx.Source, ctx.MatchedForm, MnemonicAccountKey);
        SavePendingContextButton.Content = "✓ 已收藏";
        SavePendingContextButton.IsEnabled = false;
        RenderSavedContexts(_word);
        RefreshExampleSaveButtons();
        UpdateContextReviewButtons();
        StatusText.Text = $"已收藏「{ctx.Word}」的例句";
    }

    private void UpdateSavedContextVisibility(string word)
    {
        var valid = !_recallModeActive && ValidWord(word);
        var hasItems = valid && _contextSentences.List(word, MnemonicAccountKey).Count > 0;
        if (SavedContextDivider != null) SavedContextDivider.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        if (SavedContextSection != null) SavedContextSection.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyAddContextButton != null) EmptyAddContextButton.Visibility = valid && !hasItems ? Visibility.Visible : Visibility.Collapsed;
        if (AddContextSentenceButton != null) AddContextSentenceButton.IsEnabled = valid;
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
        var accountKey = MnemonicAccountKey;
        var items = _contextSentences.List(word, accountKey);
        if (items.Count == 0)
        {
            UpdateSavedContextVisibility(word);
            return;
        }

        var cloudCount = items.Count(item => item.IsCloud);
        SavedContextHintText.Text = cloudCount > 0
            ? $"本词 {items.Count} 条（云端 {cloudCount}）"
            : $"本词 {items.Count} 条";
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

            var editBtn = new Button
            {
                Content = "编辑",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "编辑例句内容、译文或同步到墨墨云端"
            };
            editBtn.SetResourceReference(Control.ForegroundProperty, "AccentDeepBrush");
            editBtn.Click += (_, _) => OpenContextSentenceEditor(captured);

            var removeBtn = new Button
            {
                Content = "删除",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(2, 0, 0, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = captured.IsCloud ? "删除这条云端例句（同步移除云端与本机）" : "删除这条本地例句"
            };
            removeBtn.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
            removeBtn.Click += async (_, _) =>
            {
                removeBtn.IsEnabled = false;
                try { await DeleteContextSentenceAsync(captured, rethrowOnError: false); }
                finally { removeBtn.IsEnabled = true; }
            };

            actions.Children.Add(speakBtn);
            actions.Children.Add(editBtn);
            actions.Children.Add(removeBtn);
            head.Children.Add(actions);

            var badges = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var originLabel = captured.IsCloud ? "云端例句" : captured.HasUnsyncedCloudEdit ? "待同步" : "本地例句";
            var originBadge = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = originLabel, FontSize = 10.5, FontWeight = FontWeights.SemiBold }
            };
            if (captured.IsCloud)
            {
                originBadge.SetResourceReference(Border.BackgroundProperty, "SelectedBgBrush");
                originBadge.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
                ((TextBlock)originBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            }
            else if (captured.HasUnsyncedCloudEdit)
            {
                originBadge.SetResourceReference(Border.BackgroundProperty, "WarnBgBrush");
                originBadge.SetResourceReference(Border.BorderBrushProperty, "WarnBorderBrush");
                ((TextBlock)originBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "WarnFgBrush");
            }
            else
            {
                originBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                originBadge.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                ((TextBlock)originBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            }
            badges.Children.Add(originBadge);

            var meta = captured.ReviewCount > 0
                ? $"{captured.Source} · 已自测 {captured.ReviewCount} 次"
                : captured.Source;
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 5, 2),
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
            badges.Children.Add(badge);

            if (captured.Tags is { Count: > 0 })
            {
                foreach (var tag in captured.Tags.Take(4))
                {
                    var tagBadge = new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6, 1.5, 6, 1.5),
                        Margin = new Thickness(0, 0, 4, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = tag, FontSize = 10.5 }
                    };
                    tagBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                    tagBadge.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                    ((TextBlock)tagBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    badges.Children.Add(tagBadge);
                }
            }

            head.Children.Add(badges);
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

    private void MySentencesButton_Click(object sender, RoutedEventArgs e) => OpenMySentencesDialog();
    private void MistakeBookButton_Click(object sender, RoutedEventArgs e) => OpenMistakeBookDialog();

    private void OpenMistakeBookDialog()
    {
        if (!_ready) return;
        var activePad = NotepadCombo.SelectedItem as CloudNotepad;
        Func<IReadOnlyList<string>, Task<string>>? cloudAdd = _allowCloud && _tokens.HasToken && activePad != null
            ? async words =>
            {
                var full = await ReadNotepadAsync(activePad.Id);
                var existing = new HashSet<string>(NotepadContent.Words(full.Content), StringComparer.OrdinalIgnoreCase);
                var fresh = words.Where(w => !existing.Contains(w)).ToArray();
                if (fresh.Length == 0)
                    return $"所选 {words.Count} 个错题均已在「{full.Title}」中。";
                var updatedContent = NotepadContent.AddWords(full.Content, fresh);
                var saved = await _api.UpdateNotepadAsync(full, full.Title, full.Brief, updatedContent, _accountCts.Token);
                ApplyNotepadSnapshot(saved);
                return $"✓ 已将 {fresh.Length} 个错题加入「{saved.Title}」" + (words.Count > fresh.Length ? $"（跳过 {words.Count - fresh.Length} 个已存在）" : "");
            }
            : null;

        var dialog = MistakeBookDialog.Build(
            this,
            loadItems: () => _mistakeBook.List(),
            lookupEntry: w => _dictionary.Lookup(w),
            lookupWord: w => _ = LoadWordAsync(w),
            speakWord: (w, accent) => PlaySpeechAsync(w, accent),
            removeWord: w =>
            {
                var ok = _mistakeBook.Remove(w);
                UpdateContextReviewButtons();
                return ok;
            },
            startCardQuiz: (ownerWindow, words, spellingMode) =>
            {
                var sampled = _mistakeBook.PickWeightedRandom(words);
                if (sampled.Count == 0) return;
                var practice = StubbornFlashcards.Build(
                    ownerWindow,
                    sampled,
                    _dictionary,
                    _onlineDictionary,
                    word => _mnemonics.Get(word, MnemonicAccountKey).FirstOrDefault()?.Text,
                    persistProgress: false,
                    title: spellingMode ? "错题本 · 拼写随机抽测" : "错题本 · 卡片随机抽测",
                    speak: (w, accent) => PlaySpeechAsync(w, accent),
                    getMnemonics: w => _mnemonics.Get(w, MnemonicAccountKey),
                    mnemonicStore: _mnemonics,
                    preferences: _preferences,
                    spellingMistakes: _spellingMistakes,
                    contextSentences: _contextSentences,
                    accountKey: MnemonicAccountKey,
                    mistakeBook: _mistakeBook,
                    startInSpellingMode: spellingMode);
                practice.ShowDialog();
                UpdateContextReviewButtons();
            },
            addToCloudNotepad: cloudAdd,
            activeNotepadTitle: activePad?.Title);
        dialog.ShowDialog();
        UpdateContextReviewButtons();
    }

    private void OpenMySentencesDialog()
    {
        if (!_ready) return;
        var dialog = MySentencesDialog.Build(
            this,
            loadItems: () => _contextSentences.List(null, MnemonicAccountKey),
            lookupWord: w => _ = LoadWordAsync(w),
            speakSentence: (btn, text) => SpeakExampleAsync(btn, text),
            openEditor: (ownerWindow, item) => OpenContextSentenceEditor(
                item,
                explicitWord: item?.Word ?? (ValidWord(_word) ? _word : ""),
                dialogOwner: ownerWindow,
                allowEditWord: item == null),
            deleteItem: (ownerWindow, item) => DeleteContextSentenceAsync(item, rethrowOnError: false, dialogOwner: ownerWindow),
            startReview: (ownerWindow, deck) => OpenContextSentenceReviewAsync(deck, ownerWindow));
        dialog.ShowDialog();
        RefreshContextSentenceViews(_word);
    }

    private void AddContextSentenceButton_Click(object sender, RoutedEventArgs e) => OpenContextSentenceEditor(null);

    private void OpenContextSentenceEditor(
        ContextSentenceItem? original,
        string? explicitWord = null,
        Window? dialogOwner = null,
        bool allowEditWord = false)
    {
        var initialWord = (original?.Word ?? explicitWord ?? _word ?? "").Trim();
        if (!_ready || (!allowEditWord && !ValidWord(initialWord))) return;
        var account = _accountVersion;
        var accountKey = MnemonicAccountKey;
        var canSync = _allowCloud && _tokens.HasToken;
        var currentItem = original;
        var owner = dialogOwner ?? this;

        var dialog = ContextSentenceEditor.Build(
            owner,
            initialWord,
            currentItem,
            canSync,
            async (draft, sync) =>
            {
                var targetWord = ExampleText.NormalizeLookup(draft.Word).Trim();
                if (!ValidWord(targetWord))
                    throw new ArgumentException("请输入有效的英文单词或短语（最多 100 个字符）。");
                var existingCloudId = currentItem != null && currentItem.AccountKey == accountKey ? currentItem.CloudId : null;
                var localSaved = _contextSentences.SaveEdit(
                    currentItem?.Id,
                    targetWord,
                    draft.Sentence,
                    draft.Translation,
                    draft.Source,
                    draft.Tags,
                    cloudId: existingCloudId,
                    origin: "local",
                    accountKey: existingCloudId != null ? accountKey : null,
                    matchedForm: currentItem?.MatchedForm);
                currentItem = localSaved;
                RefreshContextSentenceViews(targetWord);

                if (!sync)
                {
                    StatusText.Text = existingCloudId != null
                        ? "例句已保存到本地（尚未同步到云端）。"
                        : "例句已保存到本地。";
                    return;
                }

                if (account != _accountVersion || accountKey == null)
                    throw new MaiMemoException("账号状态已变更，本地已保存，请重新打开例句后同步。");

                try
                {
                    var voc = _vocabulary?.Spelling.Equals(targetWord, StringComparison.OrdinalIgnoreCase) == true
                        ? _vocabulary
                        : await _api.ResolveWordAsync(targetWord, _accountCts.Token);
                    if (voc == null) throw new MaiMemoException("墨墨暂未收录这个单词。");
                    var synced = await _api.SavePhraseAsync(
                        voc.Id,
                        existingCloudId,
                        localSaved.Sentence,
                        localSaved.Translation,
                        localSaved.Tags,
                        localSaved.Source,
                        _accountCts.Token);
                    currentItem = _contextSentences.SaveEdit(
                        localSaved.Id,
                        targetWord,
                        synced.Text,
                        synced.Interpretation,
                        synced.Origin,
                        synced.Tags,
                        cloudId: synced.Id,
                        origin: "cloud",
                        accountKey: accountKey,
                        matchedForm: localSaved.MatchedForm);
                    _cloudCache.Remove((_accountCredentialKey ?? "offline") + ":" + targetWord.ToLowerInvariant());
                    RefreshContextSentenceViews(targetWord);
                    StatusText.Text = "例句已同步到墨墨云端。";
                }
                catch (Exception error)
                {
                    SetAccountError(error);
                    throw new MaiMemoException("已保存到本地，未能同步到墨墨：" + UserError(error));
                }
            },
            delete: currentItem == null ? null : () => DeleteContextSentenceAsync(currentItem, rethrowOnError: true, dialogOwner: owner),
            allowEditWord: allowEditWord);

        dialog.ShowDialog();
    }

    private async Task<bool> DeleteContextSentenceAsync(ContextSentenceItem item, bool rethrowOnError = false, Window? dialogOwner = null)
    {
        var accountKey = MnemonicAccountKey;
        var hasCloudCopy = !string.IsNullOrWhiteSpace(item.CloudId) && item.AccountKey == accountKey;
        if (hasCloudCopy)
        {
            if (!InputDialog.Confirm(
                    dialogOwner ?? this,
                    "删除云端例句",
                    "这条例句已同步到墨墨云端。确认后将同时从本机和墨墨云端删除，手机端也会同步移除。",
                    "确认删除"))
                return false;

            if (!_allowCloud || !_tokens.HasToken)
            {
                const string message = "当前未登录墨墨账号，无法删除云端例句。";
                StatusText.Text = message;
                if (rethrowOnError) throw new MaiMemoException(message);
                return false;
            }
            try
            {
                var voc = _vocabulary?.Spelling.Equals(item.Word, StringComparison.OrdinalIgnoreCase) == true
                    ? _vocabulary
                    : await _api.ResolveWordAsync(item.Word, _accountCts.Token);
                await _api.DeletePhraseAsync(item.CloudId!, voc?.Id, _accountCts.Token);
                _cloudCache.Remove((_accountCredentialKey ?? "offline") + ":" + item.Word.ToLowerInvariant());
            }
            catch (Exception error)
            {
                SetAccountError(error);
                var message = "删除云端例句失败：" + UserError(error);
                StatusText.Text = message;
                if (rethrowOnError) throw new MaiMemoException(message);
                return false;
            }
        }

        _contextSentences.Remove(item.Id);
        RefreshContextSentenceViews(_word);
        StatusText.Text = hasCloudCopy ? "已从本机和墨墨云端删除例句。" : "已删除例句。";
        return true;
    }

    private void RefreshContextSentenceViews(string word)
    {
        if (ValidWord(word) && word.Equals(_word, StringComparison.OrdinalIgnoreCase))
        {
            RenderSavedContexts(_word);
            RefreshExampleSaveButtons();
            UpdateContextCaptureBanner(_word);
        }
        UpdateContextReviewButtons();
    }

    private void UpdateContextReviewButtons()
    {
        var count = _contextSentences.CountForAccount(MnemonicAccountKey);
        if (MySentencesButton != null)
            MySentencesButton.ToolTip = count > 0
                ? $"我的例句（共 {count} 条 · 浏览、编辑与例句自测）"
                : "我的例句（浏览、编辑与例句自测）";
        var mistakes = _mistakeBook.Count;
        if (MistakeBookButton != null)
            MistakeBookButton.ToolTip = mistakes > 0
                ? $"错题本（当前 {mistakes} 词 · 权重随机抽测）"
                : "错题本（答错自动收录与随机抽测）";
        if (MistakeBookDot != null)
            MistakeBookDot.Visibility = mistakes > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task OpenContextSentenceReviewAsync(IReadOnlyList<ContextSentenceItem> deck, Window? dialogOwner = null)
    {
        if (deck.Count == 0)
        {
            StatusText.Text = "当前没有可用于自测的例句。";
            return;
        }

        var accountKey = MnemonicAccountKey;
        var orderedItems = deck.ToArray();
        var questions = await ContextSentenceQuiz.BuildQuestionsAsync(
            orderedItems,
            _dictionary,
            _related,
            _mnemonics,
            accountKey);
        if (questions.Count == 0) return;

        var byId = orderedItems.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var dialog = ContextSentenceQuizDialog.Build(
            dialogOwner ?? this,
            questions,
            rebuildForRetry: async retryQuestions =>
            {
                var retryItems = retryQuestions
                    .Select(q => byId.GetValueOrDefault(q.ItemId))
                    .Where(item => item != null)
                    .Cast<ContextSentenceItem>()
                    .ToArray();
                return await ContextSentenceQuiz.BuildQuestionsAsync(
                    retryItems.Length > 0 ? retryItems : orderedItems,
                    _dictionary,
                    _related,
                    _mnemonics,
                    accountKey);
            },
            onReviewed: id => _contextSentences.RecordReview(id),
            speakSentence: (btn, text) => SpeakExampleAsync(btn, text),
            lookupWord: w =>
            {
                if (dialogOwner != null && dialogOwner != this) dialogOwner.Close();
                _ = LoadWordAsync(w);
            },
            mistakeBook: _mistakeBook);
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
            var contextCount = _contextSentences.CountForAccount(MnemonicAccountKey);
            var contextBytes = _contextSentences.FileSizeBytes;
            var spellingCount = _spellingMistakes.ActiveCount;
            var spellingBytes = _spellingMistakes.FileSizeBytes;
            var mistakeCount = _mistakeBook.Count;
            var mistakeBytes = _mistakeBook.FileSizeBytes;
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
                    "我的例句（本机缓存）",
                    "本机保存与缓存的例句及自测次数，不影响墨墨云端例句。",
                    contextCount > 0 ? $"{contextCount} 条例句 · {CacheMaintenanceHost.FormatBytes(contextBytes)}" : "暂无数据",
                    contextCount > 0,
                    "仅清空本机保存与缓存的例句，不影响已同步到墨墨云端的例句。"),
                new CacheCategoryItem(
                    "mistakes",
                    "错题本记录",
                    "在卡片、拼写、例句填空与同根辨析中答错收录的单词及权重。",
                    mistakeCount > 0 ? $"{mistakeCount} 个错题 · {CacheMaintenanceHost.FormatBytes(mistakeBytes)}" : "暂无错题",
                    mistakeCount > 0,
                    "将清空本机错题本中的全部单词和权重记录。"),
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
                case "mistakes":
                    _mistakeBook.Clear();
                    UpdateContextReviewButtons();
                    return "已清空错题本记录。";
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
