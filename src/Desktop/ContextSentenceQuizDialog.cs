using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal static class ContextSentenceQuizDialog
{
    internal static Window Build(
        Window owner,
        IReadOnlyList<ContextQuizQuestion> initialQuestions,
        Func<IReadOnlyList<ContextQuizQuestion>, Task<IReadOnlyList<ContextQuizQuestion>>> rebuildForRetry,
        Action<string> onReviewed,
        Func<Button, string, Task> speakSentence,
        Action<string> lookupWord,
        MistakeBookStore? mistakeBook = null,
        ContextQuizProgressSnapshot? savedProgress = null,
        bool persistProgress = false,
        Action<ContextQuizProgressSnapshot>? onProgressChanged = null,
        Action? onClearProgress = null,
        Func<Task<IReadOnlyList<ContextQuizQuestion>>>? rebuildFreshAll = null,
        string? title = null)
    {
        var dialog = InputDialog.Shell(owner, string.IsNullOrWhiteSpace(title) ? "例句填空 · 四选一自测" : title, out _);
        dialog.Width = 780;
        dialog.Height = Math.Min(700, Math.Max(560, SystemParameters.WorkArea.Height - 55));
        dialog.MinWidth = 620;
        dialog.MinHeight = 520;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.ResizeMode = ResizeMode.CanResize;

        var pillStyle = Application.Current?.TryFindResource("PillButton") as Style;
        var accentStyle = Application.Current?.TryFindResource("AccentButton") as Style;

        var root = new Grid { Margin = new Thickness(24, 16, 24, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: header + progress
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: scrollable body
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: footer nav

        // 0. Top progress bar & score header
        var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var scoreText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        scoreText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var resetBtn = new Button
        {
            Content = "重置进度",
            Padding = new Thickness(10, 3.5, 10, 3.5),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "清除本地保存的自测进度，重新随机出题并从第 1 题开始"
        };
        topRight.Children.Add(scoreText);
        topRight.Children.Add(resetBtn);
        DockPanel.SetDock(topRight, Dock.Right);
        topDock.Children.Add(topRight);

        var progressTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        progressTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        topDock.Children.Add(progressTitle);
        topStack.Children.Add(topDock);

        var progressTrack = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), ClipToBounds = true };
        progressTrack.SetResourceReference(Border.BackgroundProperty, "AccentWashBrush");
        var progressFill = new Border { Height = 5, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2.5) };
        progressFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        progressTrack.Child = progressFill;
        topStack.Children.Add(progressTrack);
        root.Children.Add(topStack);

        // 1. Scrollable main body
        var bodyHost = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var scroll = new ScrollViewer
        {
            Content = bodyHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // 2. Bottom navigation bar
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var nextBtn = new Button
        {
            Content = "下一题 →",
            Padding = new Thickness(18, 7, 18, 7),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        };
        if (accentStyle != null) nextBtn.Style = accentStyle;
        DockPanel.SetDock(nextBtn, Dock.Right);
        footer.Children.Add(nextBtn);

        var shortcutHint = new TextBlock
        {
            Text = "快捷键：按 1–4 或 A–D 选择答案，按回车 / 空格进入下一题",
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        shortcutHint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        footer.Children.Add(shortcutHint);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        InputDialog.SetBody(dialog, root);

        var resumeActive = persistProgress && savedProgress is { Questions.Length: > 0 } && savedProgress.CurrentIndex < savedProgress.Questions.Length;
        var questions = (resumeActive ? savedProgress!.Questions : initialQuestions).ToList();
        var currentIndex = resumeActive ? Math.Clamp(savedProgress!.CurrentIndex, 0, Math.Max(0, questions.Count - 1)) : 0;
        var correctCount = resumeActive ? savedProgress!.CorrectCount : 0;
        var wrongCount = resumeActive ? savedProgress!.WrongCount : 0;
        var activePersist = persistProgress;
        ContextQuizOption? selectedOption = null;
        string? lastMistakeFeedback = null;
        var showHintTranslation = false;
        var inSummary = false;
        var wrongRecords = new List<(ContextQuizQuestion Question, ContextQuizOption Chosen)>();
        var mistakeKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        if (resumeActive)
        {
            var qById = questions.ToDictionary(q => q.ItemId, StringComparer.OrdinalIgnoreCase);
            foreach (var rec in savedProgress!.WrongRecords)
            {
                if (qById.TryGetValue(rec.ItemId, out var rq))
                {
                    var opt = rq.Options.FirstOrDefault(o => o.Label.Equals(rec.ChosenLabel, StringComparison.OrdinalIgnoreCase));
                    if (opt != null) wrongRecords.Add((rq, opt));
                }
            }
            foreach (var kv in savedProgress.MistakeKinds)
                mistakeKinds[kv.Key] = kv.Value;
        }

        void SaveProgress(int nextIndex)
        {
            if (!activePersist) return;
            if (nextIndex >= questions.Count)
            {
                onClearProgress?.Invoke();
                return;
            }
            var snapshot = new ContextQuizProgressSnapshot(
                questions.ToArray(),
                Math.Clamp(nextIndex, 0, questions.Count),
                correctCount,
                wrongCount,
                wrongRecords.Select(r => new ContextQuizWrongRecord(r.Question.ItemId, r.Chosen.Label)).ToArray(),
                new Dictionary<string, int>(mistakeKinds, StringComparer.Ordinal));
            onProgressChanged?.Invoke(snapshot);
        }

        void UpdateResetButton()
        {
            resetBtn.IsEnabled = currentIndex > 0 || selectedOption != null || inSummary;
        }

        void UpdateProgressBar()
        {
            if (questions.Count == 0) return;
            var completed = inSummary ? questions.Count : (selectedOption != null ? currentIndex + 1 : currentIndex);
            var ratio = Math.Clamp((double)completed / questions.Count, 0, 1);
            if (progressTrack.ActualWidth > 0)
                progressFill.Width = progressTrack.ActualWidth * ratio;
        }
        progressTrack.SizeChanged += (_, _) => UpdateProgressBar();

        static (Brush Bg, Brush Border, Brush Fg) KindColors(string kind, bool isDark) => kind switch
        {
            "正确答案" => (
                ThemeManager.Hex(isDark ? "#12382B" : "#E8FBF2"),
                ThemeManager.Hex(isDark ? "#23785B" : "#8DE0BA"),
                ThemeManager.Hex(isDark ? "#6BE8B4" : "#0B7A50")),
            "形近词" => (
                ThemeManager.Hex(isDark ? "#3B2814" : "#FFF6E5"),
                ThemeManager.Hex(isDark ? "#87571C" : "#F2CC82"),
                ThemeManager.Hex(isDark ? "#FBBF5C" : "#A65406")),
            "派生词" => (
                ThemeManager.Hex(isDark ? "#252142" : "#F1EEFF"),
                ThemeManager.Hex(isDark ? "#5A4CA6" : "#CEC2FA"),
                ThemeManager.Hex(isDark ? "#C4B5FD" : "#5B43B8")),
            "近义词" => (
                ThemeManager.Hex(isDark ? "#123447" : "#E6F6FF"),
                ThemeManager.Hex(isDark ? "#267099" : "#A8DCFA"),
                ThemeManager.Hex(isDark ? "#73D1FE" : "#0A76A8")),
            "你的选择" => (
                ThemeManager.Hex(isDark ? "#401922" : "#FFEBEF"),
                ThemeManager.Hex(isDark ? "#9E3548" : "#F7A8B6"),
                ThemeManager.Hex(isDark ? "#FDA4AF" : "#B91C3B")),
            _ => (
                ThemeManager.Hex(isDark ? "#24313E" : "#F2F6FA"),
                ThemeManager.Hex(isDark ? "#3B4E61" : "#D6E2ED"),
                ThemeManager.Hex(isDark ? "#A9BFCF" : "#5F768A"))
        };

        Border MakePill(string text, string colorKey)
        {
            var (bg, bd, fg) = KindColors(colorKey, ThemeManager.IsDark);
            return new Border
            {
                Background = bg,
                BorderBrush = bd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1.5, 7, 1.5),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = fg }
            };
        }

        TextBlock BuildQuestionSentence(ContextQuizQuestion q, bool answered)
        {
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                FontSize = 17.5,
                LineHeight = 30,
                TextWrapping = TextWrapping.Wrap
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");

            var parts = q.BlankedSentence.Split("______");
            var isDark = ThemeManager.IsDark;
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0) tb.Inlines.Add(new Run(parts[i]));
                if (i < parts.Length - 1)
                {
                    if (answered)
                    {
                        var (bg, _, fg) = KindColors("正确答案", isDark);
                        tb.Inlines.Add(new Run($" {q.AnswerDisplay} ")
                        {
                            FontWeight = FontWeights.Bold,
                            Foreground = fg,
                            Background = bg
                        });
                    }
                    else
                    {
                        var blank = new Run("  ______  ") { FontWeight = FontWeights.Bold };
                        blank.SetResourceReference(TextElement.ForegroundProperty, "AccentDeepBrush");
                        blank.SetResourceReference(TextElement.BackgroundProperty, "PillBgBrush");
                        tb.Inlines.Add(blank);
                    }
                }
            }
            return tb;
        }

        void RenderQuestion()
        {
            inSummary = false;
            footer.Visibility = Visibility.Visible;
            var q = questions[currentIndex];
            var answered = selectedOption != null;
            var isDark = ThemeManager.IsDark;

            progressTitle.Text = $"第 {currentIndex + 1} / {questions.Count} 题 · 例句语境填空";
            scoreText.Text = $"答对 {correctCount} · 答错 {wrongCount}";
            UpdateProgressBar();
            UpdateResetButton();

            nextBtn.IsEnabled = answered;
            nextBtn.Content = currentIndex + 1 >= questions.Count ? "查看成绩单 →" : "下一题 →";
            var progressNote = activePersist ? "（进度自动保存，可随时重置）" : "（当前为专项自测，不覆盖全部进度）";
            shortcutHint.Text = answered
                ? $"按回车 / 空格进入下一题，点击解析中的单词可直接查词 {progressNote}"
                : $"快捷键：按 1–4 或 A–D 作答 {progressNote}";

            bodyHost.Children.Clear();

            // Question card
            var qCardStack = new StackPanel();
            var qHead = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            var qHeadRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(qHeadRight, Dock.Right);

            if (!answered && !string.IsNullOrWhiteSpace(q.Translation))
            {
                var hintBtn = new Button
                {
                    Content = showHintTranslation ? "隐藏译文提示" : "看译文提示",
                    FontSize = 11,
                    Padding = new Thickness(9, 3, 9, 3),
                    Margin = new Thickness(6, 0, 0, 0)
                };
                if (pillStyle != null) hintBtn.Style = pillStyle;
                hintBtn.Click += (_, _) => { showHintTranslation = !showHintTranslation; RenderQuestion(); };
                qHeadRight.Children.Add(hintBtn);
            }
            if (answered)
            {
                var speakBtn = new Button
                {
                    Content = "朗读完整原句",
                    FontSize = 11,
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "朗读这条例句"
                };
                if (pillStyle != null) speakBtn.Style = pillStyle;
                speakBtn.Click += async (_, _) => await speakSentence(speakBtn, q.FullSentence);
                qHeadRight.Children.Add(speakBtn);
            }
            qHead.Children.Add(qHeadRight);

            var srcBadge = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 2.5, 9, 2.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = q.Source, FontSize = 11, FontWeight = FontWeights.SemiBold }
            };
            srcBadge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
            srcBadge.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
            ((TextBlock)srcBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            qHead.Children.Add(srcBadge);

            qCardStack.Children.Add(qHead);
            qCardStack.Children.Add(BuildQuestionSentence(q, answered));

            if (!string.IsNullOrWhiteSpace(q.Translation) && (answered || showHintTranslation))
            {
                var transTb = new TextBlock
                {
                    Text = q.Translation,
                    FontSize = 13.5,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                transTb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                qCardStack.Children.Add(transTb);
            }

            var qCard = new Border
            {
                Child = qCardStack,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 15, 18, 16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            qCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
            qCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            bodyHost.Children.Add(qCard);

            // 2x2 Options Grid
            var optGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            optGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            optGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var i = 0; i < q.Options.Count; i++)
            {
                var opt = q.Options[i];
                var row = (i / 2) * 2;
                var col = (i % 2) * 2;

                var isChosen = selectedOption != null && selectedOption.Label == opt.Label;
                var rowDock = new DockPanel { LastChildFill = true };

                var letterBg = answered
                    ? (opt.IsCorrect ? KindColors("正确答案", isDark).Bg : isChosen ? KindColors("你的选择", isDark).Bg : null)
                    : null;
                var letterFg = answered
                    ? (opt.IsCorrect ? KindColors("正确答案", isDark).Fg : isChosen ? KindColors("你的选择", isDark).Fg : null)
                    : null;

                var letterBadge = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Margin = new Thickness(0, 0, 11, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = opt.Label,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                if (letterBg != null && letterFg != null)
                {
                    letterBadge.Background = letterBg;
                    ((TextBlock)letterBadge.Child).Foreground = letterFg;
                }
                else
                {
                    letterBadge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
                    ((TextBlock)letterBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
                }
                DockPanel.SetDock(letterBadge, Dock.Left);
                rowDock.Children.Add(letterBadge);

                if (answered && (opt.IsCorrect || isChosen))
                {
                    var rightTag = opt.IsCorrect
                        ? MakePill("✓ 正确", "正确答案")
                        : MakePill("✗ 你的选择", "你的选择");
                    DockPanel.SetDock(rightTag, Dock.Right);
                    rowDock.Children.Add(rightTag);
                }

                var wordTb = new TextBlock
                {
                    Text = opt.DisplayText,
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                    FontSize = 15.5,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                wordTb.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                rowDock.Children.Add(wordTb);

                var btn = new Button
                {
                    Content = rowDock,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(13, 12, 13, 12),
                    BorderThickness = new Thickness(1.4),
                    Cursor = answered ? Cursors.Arrow : Cursors.Hand
                };

                if (answered)
                {
                    if (opt.IsCorrect)
                    {
                        var (bg, bd, _) = KindColors("正确答案", isDark);
                        btn.Background = bg;
                        btn.BorderBrush = bd;
                    }
                    else if (isChosen)
                    {
                        var (bg, bd, _) = KindColors("你的选择", isDark);
                        btn.Background = bg;
                        btn.BorderBrush = bd;
                    }
                    else
                    {
                        btn.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
                        btn.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
                        btn.Opacity = 0.72;
                    }
                }
                else
                {
                    btn.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
                    btn.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
                }

                btn.Click += (_, _) => SelectOption(opt);
                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                optGrid.Children.Add(btn);
            }
            bodyHost.Children.Add(optGrid);

            // Explanation Panel (after answering)
            if (answered && selectedOption != null)
            {
                var isRight = selectedOption.IsCorrect;
                var expStack = new StackPanel();

                // Verdict banner
                var (vBg, vBd, vFg) = KindColors(isRight ? "正确答案" : "你的选择", isDark);
                var verdictStack = new StackPanel();
                verdictStack.Children.Add(new TextBlock
                {
                    Text = isRight
                        ? $"✓ 回答正确！本题答案：{q.AnswerDisplay}"
                        : $"✗ 选错了（你选了 {selectedOption.Label}. {selectedOption.DisplayText}），正确答案是 {q.Options.First(o => o.IsCorrect).Label}. {q.AnswerDisplay}",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = vFg,
                    TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrWhiteSpace(lastMistakeFeedback))
                {
                    verdictStack.Children.Add(new TextBlock
                    {
                        Text = lastMistakeFeedback,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = vFg,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                verdictStack.Children.Add(new TextBlock
                {
                    Text = q.ExplanationSummary,
                    FontSize = 12.5,
                    LineHeight = 20,
                    Margin = new Thickness(0, 5, 0, 0),
                    Foreground = vFg,
                    TextWrapping = TextWrapping.Wrap
                });
                expStack.Children.Add(new Border
                {
                    Background = vBg,
                    BorderBrush = vBd,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(13, 10, 13, 10),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = verdictStack
                });

                if (!string.IsNullOrWhiteSpace(q.MnemonicHint))
                {
                    var mnemText = new TextBlock
                    {
                        Text = q.MnemonicHint,
                        FontSize = 12,
                        LineHeight = 19,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    mnemText.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
                    var mnemDock = new DockPanel();
                    var mnemPill = MakePill("助记", "近义词");
                    DockPanel.SetDock(mnemPill, Dock.Left);
                    mnemDock.Children.Add(mnemPill);
                    mnemDock.Children.Add(mnemText);

                    var mnemCard = new Border
                    {
                        CornerRadius = new CornerRadius(9),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(10, 7, 10, 7),
                        Margin = new Thickness(0, 0, 0, 10),
                        Child = mnemDock
                    };
                    mnemCard.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                    mnemCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                    expStack.Children.Add(mnemCard);
                }

                // Per-option 4-row breakdown
                var sectionHeading = new TextBlock
                {
                    Text = "四项辨析解析（点击任意单词可跳转查词）",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, 0, 0, 7)
                };
                sectionHeading.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                expStack.Children.Add(sectionHeading);

                foreach (var opt in q.Options)
                {
                    var optCaptured = opt;
                    var isUserWrong = !opt.IsCorrect && selectedOption.Label == opt.Label;
                    var rowStack = new StackPanel();
                    var rowTop = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) };

                    var wordLink = new Button
                    {
                        Content = $"{opt.Label}. {opt.DisplayText}",
                        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                        FontSize = 13.5,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(2, 0, 6, 0),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand,
                        ToolTip = $"点击查词「{opt.BaseWord}」"
                    };
                    wordLink.SetResourceReference(Control.ForegroundProperty, "AccentDeepBrush");
                    wordLink.Click += (_, _) =>
                    {
                        dialog.Close();
                        lookupWord(optCaptured.BaseWord);
                    };
                    rowTop.Children.Add(wordLink);

                    if (!string.IsNullOrWhiteSpace(opt.Phonetic))
                    {
                        var ipa = new TextBlock
                        {
                            Text = opt.Phonetic,
                            FontFamily = new FontFamily("Segoe UI"),
                            FontSize = 11.5,
                            Margin = new Thickness(0, 0, 7, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        ipa.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                        rowTop.Children.Add(ipa);
                    }

                    if (opt.IsCorrect) rowTop.Children.Add(MakePill("正确答案", "正确答案"));
                    if (isUserWrong) rowTop.Children.Add(MakePill("你的选择", "你的选择"));

                    rowStack.Children.Add(rowTop);

                    var analysisTb = new TextBlock
                    {
                        Text = opt.Analysis,
                        FontSize = 12.5,
                        LineHeight = 20,
                        TextWrapping = TextWrapping.Wrap
                    };
                    analysisTb.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
                    rowStack.Children.Add(analysisTb);

                    var itemBox = new Border
                    {
                        Child = rowStack,
                        CornerRadius = new CornerRadius(9),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(11, 8, 11, 8),
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    if (opt.IsCorrect)
                    {
                        var (bg, bd, _) = KindColors("正确答案", isDark);
                        itemBox.Background = bg;
                        itemBox.BorderBrush = bd;
                    }
                    else if (isUserWrong)
                    {
                        var (bg, bd, _) = KindColors("你的选择", isDark);
                        itemBox.Background = bg;
                        itemBox.BorderBrush = bd;
                    }
                    else
                    {
                        itemBox.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                        itemBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                    }
                    expStack.Children.Add(itemBox);
                }

                var expCard = new Border
                {
                    Child = expStack,
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(15, 13, 15, 10),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                expCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
                expCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                bodyHost.Children.Add(expCard);
                UiMotion.Reveal(expCard, distance: 4);
            }
        }

        void SelectOption(ContextQuizOption opt)
        {
            if (inSummary || selectedOption != null) return;
            selectedOption = opt;
            lastMistakeFeedback = null;
            var q = questions[currentIndex];
            onReviewed(q.ItemId);

            if (opt.IsCorrect)
            {
                correctCount++;
                if (mistakeBook != null)
                {
                    var res = mistakeBook.RecordRight(q.AnswerBaseWord, MistakeBookStore.CategorySentence);
                    if (res.Removed)
                        lastMistakeFeedback = "🎉 已熟练掌握，自动移出例句错题！";
                    else if (res.WasInBook)
                        lastMistakeFeedback = "例句错题已巩固一次，继续加油！";
                }
            }
            else
            {
                wrongCount++;
                wrongRecords.Add((q, opt));
                mistakeKinds[opt.DistractorKind] = mistakeKinds.GetValueOrDefault(opt.DistractorKind) + 1;
                if (mistakeBook != null)
                {
                    mistakeBook.RecordWrong(q.AnswerBaseWord, MistakeBookStore.CategorySentence, opt.DisplayText, q.FullSentence, q.Translation, q.ItemId);
                    lastMistakeFeedback = "已记入例句错题，后续抽测将优先巩固";
                }
            }
            SaveProgress(currentIndex + 1);
            RenderQuestion();
        }

        void RenderSummary()
        {
            inSummary = true;
            if (activePersist) onClearProgress?.Invoke();
            footer.Visibility = Visibility.Collapsed;
            var total = Math.Max(1, questions.Count);
            var accuracy = (int)Math.Round(100.0 * correctCount / total);

            progressTitle.Text = "本轮例句自测完成";
            scoreText.Text = $"答对 {correctCount} / {questions.Count} · 正确率 {accuracy}%";
            UpdateProgressBar();
            UpdateResetButton();

            bodyHost.Children.Clear();

            var kpiStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var pctBlock = new TextBlock
            {
                Text = $"{accuracy}%",
                FontSize = 38,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = accuracy >= 80 ? ThemeManager.Hex("#10B981") : accuracy >= 60 ? ThemeManager.Hex("#0EA5E9") : ThemeManager.Hex("#D97706")
            };
            var subBlock = new TextBlock
            {
                Text = $"共完成 {questions.Count} 道四选一语境填空 · 答对 {correctCount} 题 · 答错 {wrongCount} 题",
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            subBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            kpiStack.Children.Add(pctBlock);
            kpiStack.Children.Add(subBlock);

            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
            if (wrongRecords.Count > 0)
            {
                var retryWrongBtn = new Button
                {
                    Content = $"重练错题 ({wrongRecords.Count})",
                    Padding = new Thickness(16, 7, 16, 7),
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 8, 4)
                };
                if (accentStyle != null) retryWrongBtn.Style = accentStyle;
                retryWrongBtn.Click += async (_, _) =>
                {
                    retryWrongBtn.IsEnabled = false;
                    retryWrongBtn.Content = "正在重新出题…";
                    var wrongOnly = wrongRecords.Select(r => r.Question).ToArray();
                    var rebuilt = await rebuildForRetry(wrongOnly);
                    RestartWith(rebuilt, keepPersist: false);
                };
                actions.Children.Add(retryWrongBtn);
            }

            var replayBtn = new Button
            {
                Content = "再来一轮",
                Padding = new Thickness(15, 7, 15, 7),
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 8, 4)
            };
            if (pillStyle != null) replayBtn.Style = pillStyle;
            replayBtn.Click += async (_, _) =>
            {
                replayBtn.IsEnabled = false;
                replayBtn.Content = "正在重新出题…";
                var rebuilt = persistProgress && rebuildFreshAll != null
                    ? await rebuildFreshAll()
                    : await rebuildForRetry(questions);
                RestartWith(rebuilt, keepPersist: persistProgress);
            };
            actions.Children.Add(replayBtn);

            var doneBtn = new Button
            {
                Content = "返回例句列表",
                Padding = new Thickness(15, 7, 15, 7),
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 4)
            };
            doneBtn.Click += (_, _) => dialog.Close();
            actions.Children.Add(doneBtn);
            kpiStack.Children.Add(actions);

            var kpiCard = new Border
            {
                Child = kpiStack,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(22, 22, 22, 20),
                Margin = new Thickness(0, 0, 0, 14)
            };
            kpiCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
            kpiCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            bodyHost.Children.Add(kpiCard);

            if (wrongRecords.Count > 0)
            {
                var wrongTitle = new TextBlock
                {
                    Text = $"本轮错题回顾（{wrongRecords.Count} 题）",
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, 0, 0, 8)
                };
                wrongTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                bodyHost.Children.Add(wrongTitle);

                foreach (var (q, chosen) in wrongRecords)
                {
                    var capturedWord = q.AnswerBaseWord;
                    var wStack = new StackPanel();
                    var wHead = new WrapPanel { Margin = new Thickness(0, 0, 0, 5), VerticalAlignment = VerticalAlignment.Center };

                    var wordBtn = new Button
                    {
                        Content = q.AnswerBaseWord,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(9, 2.5, 9, 2.5),
                        Margin = new Thickness(0, 0, 7, 2),
                        ToolTip = $"点击查词「{q.AnswerBaseWord}」"
                    };
                    if (pillStyle != null) wordBtn.Style = pillStyle;
                    wordBtn.Click += (_, _) => { dialog.Close(); lookupWord(capturedWord); };
                    wHead.Children.Add(wordBtn);

                    wHead.Children.Add(MakePill($"正解：{q.AnswerDisplay}", "正确答案"));
                    wHead.Children.Add(MakePill($"误选：{chosen.DisplayText}", "你的选择"));
                    wStack.Children.Add(wHead);

                    var sentTb = new TextBlock
                    {
                        Text = q.FullSentence,
                        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                        FontSize = 13.5,
                        LineHeight = 21,
                        TextWrapping = TextWrapping.Wrap
                    };
                    sentTb.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
                    wStack.Children.Add(sentTb);

                    if (!string.IsNullOrWhiteSpace(q.Translation))
                    {
                        var zhTb = new TextBlock
                        {
                            Text = q.Translation,
                            FontSize = 12,
                            LineHeight = 19,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        zhTb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                        wStack.Children.Add(zhTb);
                    }

                    var wCard = new Border
                    {
                        Child = wStack,
                        CornerRadius = new CornerRadius(10),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(13, 10, 13, 10),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    wCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
                    wCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                    bodyHost.Children.Add(wCard);
                }
            }
            scroll.ScrollToTop();
        }

        void RestartWith(IReadOnlyList<ContextQuizQuestion> nextQuestions, bool keepPersist)
        {
            if (nextQuestions.Count == 0) { dialog.Close(); return; }
            activePersist = keepPersist;
            questions = nextQuestions.ToList();
            currentIndex = 0;
            correctCount = 0;
            wrongCount = 0;
            selectedOption = null;
            lastMistakeFeedback = null;
            showHintTranslation = false;
            inSummary = false;
            wrongRecords.Clear();
            mistakeKinds.Clear();
            if (activePersist) onClearProgress?.Invoke();
            scroll.ScrollToTop();
            RenderQuestion();
        }

        void GoNext()
        {
            if (inSummary || selectedOption == null) return;
            if (currentIndex + 1 >= questions.Count)
            {
                RenderSummary();
                return;
            }
            currentIndex++;
            selectedOption = null;
            lastMistakeFeedback = null;
            showHintTranslation = false;
            scroll.ScrollToTop();
            RenderQuestion();
        }

        resetBtn.Click += async (_, _) =>
        {
            if (!InputDialog.Confirm(dialog, "重置例句自测进度",
                "将清除本地保存的例句自测进度，重新随机出题并从第 1 题开始。", "重置"))
                return;
            resetBtn.IsEnabled = false;
            if (persistProgress) onClearProgress?.Invoke();
            var rebuilt = persistProgress && rebuildFreshAll != null
                ? await rebuildFreshAll()
                : await rebuildForRetry(questions);
            RestartWith(rebuilt, keepPersist: persistProgress);
        };

        nextBtn.Click += (_, _) => GoNext();

        dialog.PreviewKeyDown += (_, e) =>
        {
            if (inSummary) return;
            if (selectedOption == null)
            {
                var idx = e.Key switch
                {
                    Key.D1 or Key.NumPad1 or Key.A => 0,
                    Key.D2 or Key.NumPad2 or Key.B => 1,
                    Key.D3 or Key.NumPad3 or Key.C => 2,
                    Key.D4 or Key.NumPad4 or Key.D => 3,
                    _ => -1
                };
                if (idx >= 0 && idx < questions[currentIndex].Options.Count)
                {
                    e.Handled = true;
                    SelectOption(questions[currentIndex].Options[idx]);
                }
            }
            else if (e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                GoNext();
            }
        };

        RenderQuestion();
        return dialog;
    }
}
