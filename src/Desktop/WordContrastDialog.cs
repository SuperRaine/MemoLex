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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: compact header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: filter chip bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: scrollable cards

        // 0. Unified Compact Header
        var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var titleDock = new DockPanel();
        var speakGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
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
        speakGroup.Children.Add(ukBtn);
        speakGroup.Children.Add(usBtn);
        DockPanel.SetDock(speakGroup, Dock.Right);
        titleDock.Children.Add(speakGroup);

        var titleWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock
        {
            Text = $"{modeTitle} · {report.TargetWord}",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        titleWrap.Children.Add(titleBlock);

        if (!string.IsNullOrWhiteSpace(report.TargetPhonetic))
        {
            var ipa = new TextBlock
            {
                Text = report.TargetPhonetic,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ipa.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            titleWrap.Children.Add(ipa);
        }
        if (!string.IsNullOrWhiteSpace(report.TargetMeaning))
        {
            var meanBrief = new TextBlock
            {
                Text = report.TargetMeaning,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            meanBrief.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            titleWrap.Children.Add(meanBrief);
        }

        titleDock.Children.Add(titleWrap);
        headerStack.Children.Add(titleDock);

        if (report.Items.Count > 0)
        {
            var sub = new TextBlock
            {
                Text = isSimilar
                    ? $"共 {report.Items.Count} 个形近词 · 逐组高亮拼写差异并对比释义与巧记（点击单词跳转查词）"
                    : $"共 {report.Items.Count} 个近义词 · 对比同义下的用法方向、语域、词性与搭配（点击单词跳转查词）",
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 0)
            };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            headerStack.Children.Add(sub);
        }

        root.Children.Add(headerStack);

        // 1. Filter Chip Bar
        string? selectedWord = null;
        var chipBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(chipBar, 1);
        root.Children.Add(chipBar);

        // 2. Scrollable pairwise contrast cards
        var listPanel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var scroll = new ScrollViewer
        {
            Content = listPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 2);
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
                var card = isSimilar
                    ? BuildSimilarCard(report, item, isDark, isFocused, pillStyle,
                        w => { dialog.Close(); lookupWord(w); }, speakWord)
                    : BuildSynonymCard(report, item, isDark, isFocused, pillStyle,
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

    private static Border BuildSimilarCard(
        WordContrastReport report,
        WordContrastEntry item,
        bool isDark,
        bool isFocused,
        Style? pillStyle,
        Action<string> onLookup,
        Func<string, EnglishAccent, Task> speakWord)
    {
        var cardStack = new StackPanel();
        cardStack.Children.Add(BuildCardHeader(item, isDark, isFocused, pillStyle, onLookup, speakWord));

        // Clean pairwise visual spelling comparison (strictly 1-on-1, no multi-word mismatch!)
        var diffGrid = new Grid();
        diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var targetSpans = item.TargetSpans.Length > 0 ? item.TargetSpans : [new LetterSpan(report.TargetWord, false)];
        var candSpans = item.CandidateSpans.Length > 0 ? item.CandidateSpans : [new LetterSpan(item.Word, false)];

        var leftDiff = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var leftLabel = new TextBlock { Text = "当前词", FontSize = 10.5, Margin = new Thickness(0, 0, 0, 2) };
        leftLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        leftDiff.Children.Add(leftLabel);
        leftDiff.Children.Add(BuildHighlightedWordElement(targetSpans, isTarget: true, isDark));
        Grid.SetColumn(leftDiff, 0);
        diffGrid.Children.Add(leftDiff);

        var vs = new TextBlock
        {
            Text = "↔",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        vs.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        Grid.SetColumn(vs, 1);
        diffGrid.Children.Add(vs);

        var rightDiff = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var rightLabel = new TextBlock { Text = "形近词", FontSize = 10.5, Margin = new Thickness(0, 0, 0, 2) };
        rightLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        rightDiff.Children.Add(rightLabel);
        rightDiff.Children.Add(BuildHighlightedWordElement(candSpans, isTarget: false, isDark));
        Grid.SetColumn(rightDiff, 2);
        diffGrid.Children.Add(rightDiff);

        var diffBanner = new Border
        {
            Child = diffGrid,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 9),
            Background = ThemeManager.Hex(isDark ? "#132634" : "#F8FCFF"),
            BorderBrush = ThemeManager.Hex(isDark ? "#254960" : "#CDE6F7")
        };
        cardStack.Children.Add(diffBanner);

        // Side-by-side memory & meaning boxes for BOTH words
        var compareGrid = new Grid();
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftCue = !string.IsNullOrWhiteSpace(report.TargetMemoryHook) ? report.TargetMemoryHook : report.TargetMnemonic;
        var rightCue = !string.IsNullOrWhiteSpace(item.MemoryHook) ? item.MemoryHook : item.CandidateMnemonic;

        var leftCol = BuildSideColumn(
            report.TargetWord,
            report.TargetMeaning,
            null,
            [],
            leftCue,
            mnemonicLabel: "巧记：",
            isDark,
            isTarget: true);
        Grid.SetColumn(leftCol, 0);
        compareGrid.Children.Add(leftCol);

        var rightCol = BuildSideColumn(
            item.Word,
            item.Meaning,
            null,
            [],
            rightCue,
            mnemonicLabel: "巧记：",
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

    private static DockPanel BuildCardHeader(
        WordContrastEntry item,
        bool isDark,
        bool isFocused,
        Style? pillStyle,
        Action<string> onLookup,
        Func<string, EnglishAccent, Task> speakWord)
    {
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
        return topDock;
    }

    private static TextBlock BuildHighlightedWordElement(LetterSpan[] spans, bool isTarget, bool isDark)
    {
        var wordTb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
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
                        FontSize = 17,
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
        return wordTb;
    }

    private static Border BuildSynonymCard(
        WordContrastReport report,
        WordContrastEntry item,
        bool isDark,
        bool isFocused,
        Style? pillStyle,
        Action<string> onLookup,
        Func<string, EnglishAccent, Task> speakWord)
    {
        var cardStack = new StackPanel();
        cardStack.Children.Add(BuildCardHeader(item, isDark, isFocused, pillStyle, onLookup, speakWord));

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

        if (item.SharedSenses.Length > 0)
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

        var compareGrid = new Grid();
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        compareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPrimary = !string.IsNullOrWhiteSpace(item.TargetUsageNote)
            ? "用法：" + item.TargetUsageNote
            : (item.TargetUniqueSenses.Length > 0 ? "延伸：" + string.Join("、", item.TargetUniqueSenses) : report.TargetMeaning);
        var rightPrimary = !string.IsNullOrWhiteSpace(item.CandidateUsageNote)
            ? "用法：" + item.CandidateUsageNote
            : (item.CandidateUniqueSenses.Length > 0 ? "延伸：" + string.Join("、", item.CandidateUniqueSenses) : item.Meaning);

        var leftCol = BuildSideColumn(
            report.TargetWord,
            leftPrimary,
            report.TargetMeaning,
            item.TargetPhrases,
            report.TargetMnemonic,
            mnemonicLabel: "助记：",
            isDark,
            isTarget: true);
        Grid.SetColumn(leftCol, 0);
        compareGrid.Children.Add(leftCol);

        var rightCol = BuildSideColumn(
            item.Word,
            rightPrimary,
            item.Meaning,
            item.CandidatePhrases,
            item.CandidateMnemonic,
            mnemonicLabel: "助记：",
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

    private static Border BuildSideColumn(
        string word,
        string primaryText,
        string? fullMeaning,
        string[] phrases,
        string? mnemonic,
        string mnemonicLabel,
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
            var isHighlightLine = primaryText.StartsWith("用法：") || primaryText.StartsWith("延伸：");
            var pTb = new TextBlock
            {
                Text = primaryText,
                FontSize = 12,
                LineHeight = 19,
                FontWeight = isHighlightLine ? FontWeights.SemiBold : FontWeights.Normal,
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
                Text = mnemonicLabel + mnemonic,
                FontSize = 11.5,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = ThemeManager.Hex(isDark ? "#94A3B8" : "#475569")
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
