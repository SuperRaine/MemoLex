using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal static class WordContrastDialog
{
    internal static Window Build(
        Window owner,
        WordContrastReport report,
        Action<string> lookupWord,
        Func<string, EnglishAccent, Task> speakWord,
        string? focusWord = null)
    {
        var isSimilar = string.Equals(report.Mode, "similar", StringComparison.OrdinalIgnoreCase);
        var modeTitle = isSimilar ? "形近区分" : "近义辨析";
        var dialog = InputDialog.Shell(owner, $"{modeTitle} · {report.TargetWord}", out _);
        dialog.Width = 780;
        dialog.Height = Math.Min(700, Math.Max(540, SystemParameters.WorkArea.Height - 58));
        dialog.MinWidth = 620;
        dialog.MinHeight = 480;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.ResizeMode = ResizeMode.CanResize;

        var pillStyle = Application.Current?.TryFindResource("PillButton") as Style;
        var accentStyle = Application.Current?.TryFindResource("AccentButton") as Style;
        var isDark = ThemeManager.IsDark;

        var root = new Grid { Margin = new Thickness(22, 16, 22, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: target summary card
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: filter chip bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: scrollable cards

        // 0. Header
        var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var titleBlock = new TextBlock
        {
            Text = $"{modeTitle} · {report.TargetWord}",
            FontSize = 20,
            FontWeight = FontWeights.Bold
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        var subtitleBlock = new TextBlock
        {
            Text = report.Items.Count == 0
                ? (isSimilar ? "当前词条暂无可对比的形近词。" : "当前词条暂无可对比的近义词。")
                : (isSimilar
                    ? $"共 {report.Items.Count} 个形近词 · 高亮标出差异字母并对比读音、词性与释义（点击任意单词可跳转查词）"
                    : $"共 {report.Items.Count} 个近义词 · 对比词性边界、语义侧重与常见搭配（点击任意单词可跳转查词）"),
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        subtitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        headerStack.Children.Add(titleBlock);
        headerStack.Children.Add(subtitleBlock);
        root.Children.Add(headerStack);

        // 1. Target Word Summary Card
        var targetCard = BuildTargetSummaryCard(report, isSimilar, pillStyle, speakWord);
        Grid.SetRow(targetCard, 1);
        root.Children.Add(targetCard);

        // 2. Filter Chip Bar (for quick 1-on-1 focus)
        string? selectedWord = null;
        var chipBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(chipBar, 2);
        root.Children.Add(chipBar);

        // 3. Scrollable contrast cards
        var listPanel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var scroll = new ScrollViewer
        {
            Content = listPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 3);
        root.Children.Add(scroll);

        void RenderChips()
        {
            chipBar.Children.Clear();
            if (report.Items.Count <= 1)
            {
                chipBar.Visibility = Visibility.Collapsed;
                return;
            }
            chipBar.Visibility = Visibility.Visible;

            void AddChip(string label, string? value)
            {
                var active = string.Equals(selectedWord, value, StringComparison.OrdinalIgnoreCase);
                var btn = new Button
                {
                    Content = label,
                    FontSize = 11.5,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 5),
                    Cursor = Cursors.Hand
                };
                if (active && accentStyle != null) btn.Style = accentStyle;
                else if (pillStyle != null) btn.Style = pillStyle;
                btn.Click += (_, _) =>
                {
                    selectedWord = value;
                    RenderChips();
                    RenderCards();
                    scroll.ScrollToTop();
                };
                chipBar.Children.Add(btn);
            }

            AddChip($"全部 ({report.Items.Count})", null);
            foreach (var item in report.Items)
                AddChip(item.Word, item.Word);
        }

        void RenderCards()
        {
            listPanel.Children.Clear();
            if (report.Items.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = isSimilar ? "暂未收录该词的形近词。" : "暂未收录该词的近义词。",
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                listPanel.Children.Add(empty);
                return;
            }

            var visible = string.IsNullOrWhiteSpace(selectedWord)
                ? report.Items
                : report.Items.Where(x => x.Word.Equals(selectedWord, StringComparison.OrdinalIgnoreCase)).ToArray();

            foreach (var item in visible)
            {
                var isFocused = !string.IsNullOrWhiteSpace(focusWord) &&
                    item.Word.Equals(focusWord.Trim(), StringComparison.OrdinalIgnoreCase);
                var card = BuildContrastCard(report, item, isSimilar, isDark, isFocused, pillStyle,
                    w => { dialog.Close(); lookupWord(w); }, speakWord);
                listPanel.Children.Add(card);
            }
        }

        RenderChips();
        RenderCards();

        InputDialog.SetBody(dialog, root);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };
        return dialog;
    }

    private static Border BuildTargetSummaryCard(
        WordContrastReport report,
        bool isSimilar,
        Style? pillStyle,
        Func<string, EnglishAccent, Task> speakWord)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ukBtn = new Button
        {
            Content = "英音",
            FontSize = 11,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 5, 0),
            ToolTip = $"朗读 {report.TargetWord}（英音）"
        };
        if (pillStyle != null) ukBtn.Style = pillStyle;
        ukBtn.Click += async (_, _) => await speakWord(report.TargetWord, EnglishAccent.British);

        var usBtn = new Button
        {
            Content = "美音",
            FontSize = 11,
            Padding = new Thickness(9, 3, 9, 3),
            ToolTip = $"朗读 {report.TargetWord}（美音）"
        };
        if (pillStyle != null) usBtn.Style = pillStyle;
        usBtn.Click += async (_, _) => await speakWord(report.TargetWord, EnglishAccent.American);

        rightPanel.Children.Add(ukBtn);
        rightPanel.Children.Add(usBtn);
        DockPanel.SetDock(rightPanel, Dock.Right);
        head.Children.Add(rightPanel);

        var leftWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        leftWrap.Children.Add(MakeSoftPill("当前词", isAccent: true));
        var wordTb = new TextBlock
        {
            Text = report.TargetWord,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        wordTb.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        leftWrap.Children.Add(wordTb);

        if (!string.IsNullOrWhiteSpace(report.TargetPhonetic))
        {
            var ipaTb = new TextBlock
            {
                Text = report.TargetPhonetic,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ipaTb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            leftWrap.Children.Add(ipaTb);
        }

        if (!string.IsNullOrWhiteSpace(report.TargetPrimaryPos))
            leftWrap.Children.Add(MakeSoftPill(report.TargetPrimaryPos, isAccent: false));

        head.Children.Add(leftWrap);
        stack.Children.Add(head);

        if (!string.IsNullOrWhiteSpace(report.TargetMeaning))
        {
            var meanTb = new TextBlock
            {
                Text = report.TargetMeaning,
                FontSize = 13,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap
            };
            meanTb.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            stack.Children.Add(meanTb);
        }

        if (!isSimilar && report.TargetPhrases.Length > 0)
        {
            var phraseRow = new WrapPanel { Margin = new Thickness(0, 7, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock
            {
                Text = "常见搭配：",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            lbl.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            phraseRow.Children.Add(lbl);
            foreach (var p in report.TargetPhrases)
                phraseRow.Children.Add(MakeSoftPill(p, isAccent: false));
            stack.Children.Add(phraseRow);
        }

        if (!string.IsNullOrWhiteSpace(report.TargetMnemonic))
        {
            var mnemTb = new TextBlock
            {
                Text = "助记：" + report.TargetMnemonic,
                FontSize = 11.5,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            mnemTb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(mnemTb);
        }

        var card = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 11)
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
        return card;
    }

    private static Border BuildContrastCard(
        WordContrastReport report,
        WordContrastEntry item,
        bool isSimilar,
        bool isDark,
        bool isFocused,
        Style? pillStyle,
        Action<string> onLookup,
        Func<string, EnglishAccent, Task> speakWord)
    {
        var cardStack = new StackPanel();

        // Top row: Candidate Word + IPA + Speak + PosSummary
        var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (!string.IsNullOrWhiteSpace(item.PosSummary))
        {
            var posDiff = item.PosSummary.Contains("不同");
            rightStack.Children.Add(MakeStatusPill(
                item.PosSummary,
                posDiff ? (isDark ? "#2D2110" : "#FFFBEB") : (isDark ? "#132936" : "#F0F9FF"),
                posDiff ? (isDark ? "#784C12" : "#FCD34D") : (isDark ? "#234C63" : "#BAE6FD"),
                posDiff ? (isDark ? "#FBBF24" : "#B45309") : (isDark ? "#7DD3FC" : "#0369A1")));
        }

        var speakBtn = new Button
        {
            Content = "朗读",
            FontSize = 11,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = $"朗读 {item.Word}"
        };
        if (pillStyle != null) speakBtn.Style = pillStyle;
        speakBtn.Click += async (_, _) => await speakWord(item.Word, EnglishAccent.British);
        rightStack.Children.Add(speakBtn);

        DockPanel.SetDock(rightStack, Dock.Right);
        topDock.Children.Add(rightStack);

        var leftWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var wordBtn = new Button
        {
            Content = item.Word,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(2, 0, 6, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = $"点击查词「{item.Word}」"
        };
        wordBtn.SetResourceReference(Control.ForegroundProperty, "AccentDeepBrush");
        wordBtn.Click += (_, _) => onLookup(item.Word);
        leftWrap.Children.Add(wordBtn);

        if (!string.IsNullOrWhiteSpace(item.Phonetic))
        {
            var ipa = new TextBlock
            {
                Text = item.Phonetic,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ipa.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
            leftWrap.Children.Add(ipa);
        }

        if (isFocused)
            leftWrap.Children.Add(MakeSoftPill("已定位", isAccent: true));

        topDock.Children.Add(leftWrap);
        cardStack.Children.Add(topDock);

        // Similar mode: Letter-by-letter spelling diff strip
        if (isSimilar && item.TargetSpans.Length > 0 && item.CandidateSpans.Length > 0)
        {
            var diffStack = new StackPanel();
            var diffGrid = new Grid();
            diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftBox = BuildSpellingRow("当前词", item.TargetSpans, isTarget: true, isDark);
            Grid.SetColumn(leftBox, 0);
            diffGrid.Children.Add(leftBox);

            var arrow = new TextBlock
            {
                Text = "↔",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
            Grid.SetColumn(arrow, 1);
            diffGrid.Children.Add(arrow);

            var rightBox = BuildSpellingRow("形近词", item.CandidateSpans, isTarget: false, isDark);
            Grid.SetColumn(rightBox, 2);
            diffGrid.Children.Add(rightBox);

            diffStack.Children.Add(diffGrid);

            if (!string.IsNullOrWhiteSpace(item.SpellingDiffSummary))
            {
                var tipTb = new TextBlock
                {
                    Text = "拼写差异：" + item.SpellingDiffSummary,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 7, 0, 0),
                    Foreground = ThemeManager.Hex(isDark ? "#FBBF24" : "#B45309"),
                    TextWrapping = TextWrapping.Wrap
                };
                diffStack.Children.Add(tipTb);
            }

            var diffCard = new Border
            {
                Child = diffStack,
                Background = ThemeManager.Hex(isDark ? "#1C1A15" : "#FFFDF5"),
                BorderBrush = ThemeManager.Hex(isDark ? "#4D3B1A" : "#FDE68A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 9)
            };
            cardStack.Children.Add(diffCard);
        }

        // Contrastive Verdict Box
        if (!string.IsNullOrWhiteSpace(item.Verdict))
        {
            var verdictTb = new TextBlock
            {
                Text = item.Verdict,
                FontSize = 12.5,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap
            };
            verdictTb.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");

            var verdictBox = new Border
            {
                Child = verdictTb,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 8, 11, 8),
                Margin = new Thickness(0, 0, 0, 9)
            };
            verdictBox.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            verdictBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            cardStack.Children.Add(verdictBox);
        }

        // Synonym mode: Shared Senses Strip
        if (!isSimilar && item.SharedSenses.Length > 0)
        {
            var sharedRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock
            {
                Text = "共通义：",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            lbl.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            sharedRow.Children.Add(lbl);
            foreach (var s in item.SharedSenses)
            {
                sharedRow.Children.Add(MakeStatusPill(
                    s,
                    isDark ? "#0D2B24" : "#ECFDF5",
                    isDark ? "#1B5E4E" : "#6EE7B7",
                    isDark ? "#34D399" : "#047857"));
            }
            cardStack.Children.Add(sharedRow);
        }

        // Side-by-side 2-column comparison (Target vs Candidate)
        var compareGrid = new Grid();
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftCol = BuildSideColumn(
            report.TargetWord,
            isSimilar ? report.TargetMeaning : (item.TargetUniqueSenses.Length > 0 ? "侧重：" + string.Join("、", item.TargetUniqueSenses) : report.TargetMeaning),
            isSimilar ? null : report.TargetMeaning,
            isSimilar ? [] : item.TargetPhrases,
            isSimilar ? report.TargetMnemonic : null,
            isDark,
            isTarget: true);
        Grid.SetColumn(leftCol, 0);
        compareGrid.Children.Add(leftCol);

        var rightCol = BuildSideColumn(
            item.Word,
            isSimilar ? item.Meaning : (item.CandidateUniqueSenses.Length > 0 ? "侧重：" + string.Join("、", item.CandidateUniqueSenses) : item.Meaning),
            isSimilar ? null : item.Meaning,
            isSimilar ? [] : item.CandidatePhrases,
            item.CandidateMnemonic,
            isDark,
            isTarget: false);
        Grid.SetColumn(rightCol, 2);
        compareGrid.Children.Add(rightCol);

        cardStack.Children.Add(compareGrid);

        var outer = new Border
        {
            Child = cardStack,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(isFocused ? 1.4 : 1),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        outer.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        outer.SetResourceReference(Border.BorderBrushProperty, isFocused ? "AccentBrush" : "BorderBrush");
        return outer;
    }

    private static StackPanel BuildSpellingRow(string label, LetterSpan[] spans, bool isTarget, bool isDark)
    {
        var wrap = new StackPanel();
        var cap = new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };
        cap.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        wrap.Children.Add(cap);

        var wordTb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontSize = 17.5,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        wordTb.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");

        var hlBg = isTarget
            ? ThemeManager.Hex(isDark ? "#15364A" : "#DBEAFE")
            : ThemeManager.Hex(isDark ? "#431D25" : "#FFE4E6");
        var hlFg = isTarget
            ? ThemeManager.Hex(isDark ? "#7DD3FC" : "#0369A1")
            : ThemeManager.Hex(isDark ? "#FB7185" : "#BE123C");

        foreach (var span in spans)
        {
            if (span.Highlight)
            {
                var badge = new Border
                {
                    Background = hlBg,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(3, 0, 3, 0),
                    Margin = new Thickness(1, 0, 1, 0),
                    Child = new TextBlock
                    {
                        Text = span.Text,
                        FontFamily = new FontFamily("Consolas, Segoe UI"),
                        FontSize = 17.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = hlFg
                    }
                };
                wordTb.Inlines.Add(new InlineUIContainer(badge) { BaselineAlignment = BaselineAlignment.Center });
            }
            else
            {
                wordTb.Inlines.Add(new Run(span.Text));
            }
        }
        wrap.Children.Add(wordTb);
        return wrap;
    }

    private static Border BuildSideColumn(
        string word,
        string primaryText,
        string? fullMeaning,
        string[] phrases,
        string? mnemonic,
        bool isDark,
        bool isTarget)
    {
        var col = new StackPanel();
        var title = new TextBlock
        {
            Text = word,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, isTarget ? "AccentDeepBrush" : "InkStrongBrush");
        col.Children.Add(title);

        if (!string.IsNullOrWhiteSpace(primaryText))
        {
            var pTb = new TextBlock
            {
                Text = primaryText,
                FontSize = 12,
                LineHeight = 19,
                FontWeight = !string.IsNullOrWhiteSpace(fullMeaning) && primaryText.StartsWith("侧重：") ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            };
            pTb.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            col.Children.Add(pTb);
        }

        if (!string.IsNullOrWhiteSpace(fullMeaning) && !string.Equals(fullMeaning, primaryText, StringComparison.Ordinal))
        {
            var mTb = new TextBlock
            {
                Text = fullMeaning,
                FontSize = 11.5,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            mTb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            col.Children.Add(mTb);
        }

        if (phrases.Length > 0)
        {
            var pWrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var p in phrases)
                pWrap.Children.Add(MakeSoftPill(p, isAccent: false));
            col.Children.Add(pWrap);
        }

        if (!string.IsNullOrWhiteSpace(mnemonic))
        {
            var mnemTb = new TextBlock
            {
                Text = "助记：" + mnemonic,
                FontSize = 11,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = ThemeManager.Hex(isDark ? "#94A3B8" : "#64748B")
            };
            col.Children.Add(mnemTb);
        }

        var box = new Border
        {
            Child = col,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8)
        };
        box.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return box;
    }

    private static Border MakeSoftPill(string text, bool isAccent)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, isAccent ? "AccentDeepBrush" : "MutedBrush");
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 5, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = tb
        };
        border.SetResourceReference(Border.BackgroundProperty, isAccent ? "PillBgBrush" : "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, isAccent ? "PillBorderBrush" : "BorderBrush");
        return border;
    }

    private static Border MakeStatusPill(string text, string bgHex, string bdHex, string fgHex) =>
        new()
        {
            Background = ThemeManager.Hex(bgHex),
            BorderBrush = ThemeManager.Hex(bdHex),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 5, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeManager.Hex(fgHex)
            }
        };
}
