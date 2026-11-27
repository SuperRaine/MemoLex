using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal static class MorphemeGroupPracticeDialog
{
    internal static Window Build(
        Window owner,
        MorphemePracticeGroup group,
        Func<string, EnglishAccent, Task>? speak = null,
        Action<string>? lookupWord = null)
    {
        var dialog = InputDialog.Shell(owner, $"词根「{group.RootForm}」同组辨析", out _);
        dialog.Width = 740;
        dialog.Height = Math.Min(650, SystemParameters.WorkArea.Height - 60);
        dialog.MinWidth = 580;
        dialog.MinHeight = 460;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.ResizeMode = ResizeMode.CanResize;
        System.Windows.Shell.WindowChrome.GetWindowChrome(dialog).ResizeBorderThickness = new Thickness(6);

        var root = new Grid { Margin = new Thickness(22, 16, 22, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        InputDialog.SetBody(dialog, root);

        // Header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var headerRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(headerRight, Dock.Right);
        var scoreBadgeText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        scoreBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        var scoreBadge = new Border
        {
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = scoreBadgeText
        };
        scoreBadge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
        scoreBadge.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
        var restartBtn = new Button { Content = "重新洗牌", FontSize = 11.5, Padding = new Thickness(11, 5, 11, 5) };
        headerRight.Children.Add(scoreBadge);
        headerRight.Children.Add(restartBtn);
        header.Children.Add(headerRight);

        var titleStack = new StackPanel();
        var titleText = new TextBlock
        {
            Text = $"词根 {group.RootForm}（{group.RootMeaning}）· 练这组",
            FontSize = 19,
            FontWeight = FontWeights.Bold
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        var subtitleText = InputDialog.Text($"共 {group.Members.Count} 个同源词并排对比，优先挑选你查过或收藏过的词。");
        subtitleText.FontSize = 11.5;
        subtitleText.Margin = new Thickness(0, 2, 0, 0);
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(subtitleText);
        header.Children.Add(titleStack);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Question card
        var promptCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 14, 18, 14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        promptCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        promptCard.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
        var promptStack = new StackPanel();
        var promptTopRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var hintToggleBtn = new Button
        {
            Content = "词缀提示",
            FontSize = 11,
            Padding = new Thickness(9, 3, 9, 3),
            ToolTip = "查看本题正确答案的词缀线索"
        };
        if (Application.Current.TryFindResource("PillButton") is Style pillStyle) hintToggleBtn.Style = pillStyle;
        DockPanel.SetDock(hintToggleBtn, Dock.Right);
        promptTopRow.Children.Add(hintToggleBtn);

        var stepLabel = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        stepLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        promptTopRow.Children.Add(stepLabel);
        promptStack.Children.Add(promptTopRow);

        var questionText = new TextBlock
        {
            FontSize = 18.5,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 27
        };
        questionText.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        promptStack.Children.Add(questionText);

        var affixHintText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        affixHintText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        promptStack.Children.Add(affixHintText);

        var feedbackText = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        promptStack.Children.Add(feedbackText);

        promptCard.Child = promptStack;
        Grid.SetRow(promptCard, 1);
        root.Children.Add(promptCard);

        // Members side-by-side grid
        var cardsGrid = new UniformGrid { Columns = group.Members.Count <= 4 ? 2 : 3, VerticalAlignment = VerticalAlignment.Top };
        var cardsScroll = new ScrollViewer
        {
            Content = cardsGrid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(cardsScroll, 2);
        root.Children.Add(cardsScroll);

        // Footer
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var nextBtn = new Button
        {
            Content = "下一题 →",
            Padding = new Thickness(18, 8, 18, 8),
            FontSize = 12.5,
            Visibility = Visibility.Collapsed
        };
        if (Application.Current.TryFindResource("AccentButton") is Style accentStyle) nextBtn.Style = accentStyle;
        DockPanel.SetDock(nextBtn, Dock.Right);
        footer.Children.Add(nextBtn);

        var footerHint = InputDialog.Text("快捷键：数字键 1–6 选择单词 · 空格/回车进入下一题");
        footerHint.FontSize = 11;
        footerHint.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(footerHint);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        // Quiz state
        var rng = new Random();
        var questions = new List<MorphemePracticeMember>();
        var currentIndex = 0;
        var firstTryCorrect = 0;
        var answeredCorrectly = false;
        var missedCurrent = false;
        var wrongPicked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finished = false;

        void StartQuiz()
        {
            questions = group.Members.OrderBy(_ => rng.Next()).ToList();
            currentIndex = 0;
            firstTryCorrect = 0;
            answeredCorrectly = false;
            missedCurrent = false;
            wrongPicked.Clear();
            finished = false;
            Render();
        }

        UIElement BuildSegmentChips(MorphemePracticeMember member, bool showMeanings)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            for (var i = 0; i < member.Segments.Count; i++)
            {
                if (i > 0)
                {
                    wrap.Children.Add(new TextBlock
                    {
                        Text = "+",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = ThemeManager.Hex("#7AA9C7"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 2)
                    });
                }
                var seg = member.Segments[i];
                var chipStack = new StackPanel { Orientation = Orientation.Vertical };
                var formLbl = new TextBlock
                {
                    Text = seg.Form,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = seg.IsTargetRoot
                        ? ThemeManager.Res("MutedBrush", "#5B7A91", "#9AB3C7")
                        : ThemeManager.Hex(ThemeManager.IsDark ? "#67D2FF" : "#0A7FB3")
                };
                chipStack.Children.Add(formLbl);
                if (showMeanings && !string.IsNullOrWhiteSpace(seg.Meaning))
                {
                    var segMean = new TextBlock
                    {
                        Text = seg.Meaning,
                        FontSize = 10,
                        Foreground = ThemeManager.Res("MutedBrush", "#607C93", "#9AB3C7")
                    };
                    chipStack.Children.Add(segMean);
                }
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 0, 2),
                    Child = chipStack
                };
                if (seg.IsTargetRoot)
                {
                    pill.SetResourceReference(Border.BackgroundProperty, "CardAltBrush");
                    pill.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                }
                else
                {
                    pill.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
                    pill.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
                }
                wrap.Children.Add(pill);
            }
            return wrap;
        }

        void Render()
        {
            cardsGrid.Children.Clear();
            affixHintText.Visibility = Visibility.Collapsed;
            affixHintText.Text = "";

            if (finished)
            {
                stepLabel.Text = "本组词根辨析完成";
                scoreBadgeText.Text = $"一次答对 {firstTryCorrect} / {questions.Count}";
                questionText.Text = firstTryCorrect == questions.Count
                    ? $"全部掌握！「{group.RootForm}」这组 {group.Members.Count} 个词一次答对。"
                    : $"完成本轮练习（一次答对 {firstTryCorrect} / {questions.Count}），下方可对比复习全部词缀差异。";
                hintToggleBtn.Visibility = Visibility.Collapsed;
                feedbackText.Visibility = Visibility.Collapsed;
                nextBtn.Content = "再练一遍";
                nextBtn.Visibility = Visibility.Visible;
                footerHint.Text = "点击任意卡片上的「查词」可跳转主界面查看完整例句与助记。";

                foreach (var member in group.Members)
                    cardsGrid.Children.Add(BuildMemberCard(member, -1, revealAll: true));
                return;
            }

            var current = questions[currentIndex];
            stepLabel.Text = $"第 {currentIndex + 1} / {questions.Count} 题 · 选择对应的英文单词";
            scoreBadgeText.Text = $"一次答对 {firstTryCorrect} / {currentIndex + (answeredCorrectly ? 1 : 0)}";
            questionText.Text = $"哪个词表示「{current.PromptMeaning}」？";
            hintToggleBtn.Visibility = answeredCorrectly ? Visibility.Collapsed : Visibility.Visible;
            nextBtn.Content = currentIndex + 1 < questions.Count ? "下一题 →" : "查看总结";
            nextBtn.Visibility = answeredCorrectly ? Visibility.Visible : Visibility.Collapsed;
            footerHint.Text = answeredCorrectly
                ? "按空格或回车进入下一题，也可点卡片上的「查词」深入查看"
                : "快捷键：数字键 1–6 选择单词 · 对比词缀前缀含义来判断";

            if (!answeredCorrectly && wrongPicked.Count == 0)
            {
                feedbackText.Visibility = Visibility.Collapsed;
            }

            for (var i = 0; i < group.Members.Count; i++)
                cardsGrid.Children.Add(BuildMemberCard(group.Members[i], i, revealAll: answeredCorrectly));
        }

        UIElement BuildMemberCard(MorphemePracticeMember member, int index, bool revealAll)
        {
            var current = finished ? null : questions[currentIndex];
            var isAnswer = current != null && member.Word.Equals(current.Word, StringComparison.OrdinalIgnoreCase);
            var isWrong = wrongPicked.Contains(member.Word);

            var stack = new StackPanel();
            var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (index >= 0)
            {
                var numTag = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = (index + 1).ToString(),
                        FontSize = 10.5, FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = ThemeManager.Res("MutedBrush", "#607C93", "#9AB3C7")
                    }
                };
                numTag.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
                DockPanel.SetDock(numTag, Dock.Left);
                topBar.Children.Add(numTag);
            }

            var rightBadges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(rightBadges, Dock.Right);
            if (member.IsUserWord)
            {
                var userTag = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(5, 1.5, 5, 1.5),
                    Margin = new Thickness(4, 0, 0, 0),
                    Background = ThemeManager.Res("WarnBgBrush", "#FFF7E6", "#332614"),
                    Child = new TextBlock
                    {
                        Text = "查过/收藏",
                        FontSize = 9.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = ThemeManager.Res("WarnFgBrush", "#9A5207", "#F8C76A")
                    }
                };
                rightBadges.Children.Add(userTag);
            }
            if ((revealAll || isWrong) && lookupWord != null)
            {
                var jumpBtn = new Button
                {
                    Content = "查词",
                    FontSize = 10,
                    Padding = new Thickness(6, 1.5, 6, 1.5),
                    Margin = new Thickness(4, 0, 0, 0),
                    ToolTip = $"在主界面查看「{member.Word}」"
                };
                jumpBtn.Click += (_, e) =>
                {
                    e.Handled = true;
                    dialog.Close();
                    lookupWord(member.Word);
                };
                rightBadges.Children.Add(jumpBtn);
            }
            topBar.Children.Add(rightBadges);

            var wordTb = new TextBlock
            {
                Text = member.Word,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            wordTb.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
            topBar.Children.Add(wordTb);
            stack.Children.Add(topBar);

            stack.Children.Add(BuildSegmentChips(member, showMeanings: revealAll || isWrong));

            if (revealAll || isWrong)
            {
                var meaningTb = new TextBlock
                {
                    Text = member.Meaning,
                    FontSize = 12,
                    LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                meaningTb.SetResourceReference(TextBlock.ForegroundProperty, isAnswer && revealAll ? "InkStrongBrush" : "MutedBrush");
                stack.Children.Add(meaningTb);
            }
            else
            {
                var maskedTb = new TextBlock
                {
                    Text = "点击选择此词",
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                maskedTb.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                stack.Children.Add(maskedTb);
            }

            var btn = new Button
            {
                Content = stack,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(4),
                MinHeight = 108
            };
            if (revealAll && isAnswer)
            {
                btn.Background = ThemeManager.Hex(ThemeManager.IsDark ? "#123528" : "#E6FAF1");
                btn.BorderBrush = ThemeManager.Hex(ThemeManager.IsDark ? "#1E6F50" : "#5AD1A4");
                btn.BorderThickness = new Thickness(1.6);
            }
            else if (isWrong)
            {
                btn.Background = ThemeManager.Hex(ThemeManager.IsDark ? "#3A1A22" : "#FFF1F3");
                btn.BorderBrush = ThemeManager.Hex(ThemeManager.IsDark ? "#8A3346" : "#F5A3B2");
                btn.BorderThickness = new Thickness(1.3);
            }
            else
            {
                btn.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
                btn.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            }

            btn.Click += (_, _) =>
            {
                if (finished || answeredCorrectly) return;
                SelectChoice(member);
            };
            return btn;
        }

        void SelectChoice(MorphemePracticeMember picked)
        {
            if (finished || answeredCorrectly) return;
            var current = questions[currentIndex];
            if (picked.Word.Equals(current.Word, StringComparison.OrdinalIgnoreCase))
            {
                answeredCorrectly = true;
                if (!missedCurrent) firstTryCorrect++;
                feedbackText.Text = $"✓ 正确！{current.Word}：{current.Explanation}";
                feedbackText.Foreground = ThemeManager.Hex(ThemeManager.IsDark ? "#34D399" : "#0E7C55");
                feedbackText.Visibility = Visibility.Visible;
                if (speak != null) _ = speak(current.Word, EnglishAccent.American);
                Render();
                nextBtn.Focus();
            }
            else
            {
                missedCurrent = true;
                wrongPicked.Add(picked.Word);
                feedbackText.Text = $"✗ {picked.Word} 表示「{picked.PromptMeaning}」，再看看其他词缀！";
                feedbackText.Foreground = ThemeManager.Hex(ThemeManager.IsDark ? "#FB7185" : "#C4253F");
                feedbackText.Visibility = Visibility.Visible;
                Render();
            }
        }

        void Advance()
        {
            if (finished)
            {
                StartQuiz();
                return;
            }
            if (!answeredCorrectly) return;
            currentIndex++;
            answeredCorrectly = false;
            missedCurrent = false;
            wrongPicked.Clear();
            if (currentIndex >= questions.Count) finished = true;
            Render();
        }

        hintToggleBtn.Click += (_, _) =>
        {
            if (finished || answeredCorrectly) return;
            var current = questions[currentIndex];
            var affixes = current.Segments.Where(s => !s.IsTargetRoot).ToArray();
            affixHintText.Text = affixes.Length > 0
                ? "词缀线索：" + string.Join(" ＋ ", affixes.Select(a => string.IsNullOrWhiteSpace(a.Meaning) ? a.Form : $"{a.Form}（{a.Meaning}）"))
                : $"构词线索：{current.Explanation}";
            affixHintText.Visibility = Visibility.Visible;
        };

        restartBtn.Click += (_, _) => StartQuiz();
        nextBtn.Click += (_, _) => Advance();

        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter && (answeredCorrectly || finished))
            {
                e.Handled = true;
                if (!e.IsRepeat) Advance();
                return;
            }
            if (!finished && !answeredCorrectly && Keyboard.Modifiers == ModifierKeys.None)
            {
                var idx = e.Key switch
                {
                    Key.D1 or Key.NumPad1 => 0,
                    Key.D2 or Key.NumPad2 => 1,
                    Key.D3 or Key.NumPad3 => 2,
                    Key.D4 or Key.NumPad4 => 3,
                    Key.D5 or Key.NumPad5 => 4,
                    Key.D6 or Key.NumPad6 => 5,
                    _ => -1
                };
                if (idx >= 0 && idx < group.Members.Count)
                {
                    e.Handled = true;
                    if (!e.IsRepeat) SelectChoice(group.Members[idx]);
                }
            }
        };

        StartQuiz();
        return dialog;
    }
}
