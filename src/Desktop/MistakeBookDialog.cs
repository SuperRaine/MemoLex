using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal static class MistakeBookDialog
{
    private enum FilterKind { All, HighWeight, Spelling, Sentence, Flashcard }

    internal static Window Build(
        Window owner,
        Func<IReadOnlyList<MistakeBookItem>> loadItems,
        Func<string, DictionaryEntry?> lookupEntry,
        Action<string> lookupWord,
        Func<string, EnglishAccent, Task> speakWord,
        Func<string, bool> removeWord,
        Action<Window, IReadOnlyList<string>, bool> startCardQuiz,
        Func<IReadOnlyList<string>, Task<string>>? addToCloudNotepad = null,
        string? activeNotepadTitle = null)
    {
        var dialog = InputDialog.Shell(owner, "错题本", out _);
        dialog.Width = 790;
        dialog.Height = Math.Min(680, Math.Max(540, SystemParameters.WorkArea.Height - 60));
        dialog.MinWidth = 640;
        dialog.MinHeight = 490;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.ResizeMode = ResizeMode.CanResize;

        var pillStyle = Application.Current?.TryFindResource("PillButton") as Style;
        var accentStyle = Application.Current?.TryFindResource("AccentButton") as Style;

        var root = new Grid { Margin = new Thickness(22, 16, 22, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: rule banner + search + filters
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: scrollable list
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: footer status

        // 0. Header
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 11) };
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        Button? cloudAddBtn = null;
        if (addToCloudNotepad != null)
        {
            cloudAddBtn = new Button
            {
                Content = "加入云词本",
                Padding = new Thickness(11, 6, 11, 6),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 7, 0),
                ToolTip = string.IsNullOrWhiteSpace(activeNotepadTitle)
                    ? "将当前列表中的错题单词批量加入选中的云词本"
                    : $"将当前列表中的错题单词批量加入「{activeNotepadTitle}」"
            };
            if (pillStyle != null) cloudAddBtn.Style = pillStyle;
            headerActions.Children.Add(cloudAddBtn);
        }

        var spellQuizBtn = new Button
        {
            Content = "拼写抽测",
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "按错题权重加权随机抽测，直接进入拼写默写模式（答对权重 -1，答错 +3）"
        };
        if (pillStyle != null) spellQuizBtn.Style = pillStyle;
        headerActions.Children.Add(spellQuizBtn);

        var randomQuizBtn = new Button
        {
            Content = "随机抽测",
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "像卡片一样按权重加权随机抽测错题（支持翻卡与拼写切换；答对权重 -1，归 0 自动移出）"
        };
        if (accentStyle != null) randomQuizBtn.Style = accentStyle;
        headerActions.Children.Add(randomQuizBtn);

        DockPanel.SetDock(headerActions, Dock.Right);
        headerDock.Children.Add(headerActions);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock { Text = "错题本", FontSize = 21, FontWeight = FontWeights.Bold };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        var summaryText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
        summaryText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        titleStack.Children.Add(titleBlock);
        titleStack.Children.Add(summaryText);
        headerDock.Children.Add(titleStack);
        root.Children.Add(headerDock);

        // 1. Rules + Search + Filter chips
        var toolbarCard = new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        toolbarCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        toolbarCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var toolbarStack = new StackPanel();
        var ruleStrip = new TextBlock
        {
            Text = "权重规则：答错 / 拼错自动入本（权重 +3）· 答对 / 拼对（权重 -1）· 权重归 0 自动移出 · 抽测时高权重词优先随机出现",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(1, 0, 0, 8)
        };
        ruleStrip.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        toolbarStack.Children.Add(ruleStrip);

        var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        var searchBox = new TextBox
        {
            Height = 33,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 2, 10, 2),
            ToolTip = "按单词、释义或误拼记录筛选"
        };
        var searchOverlay = new TextBlock
        {
            Text = "筛选错题单词、释义或误拼记录…",
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsHitTestVisible = false
        };
        searchOverlay.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        searchHost.Children.Add(searchBox);
        searchHost.Children.Add(searchOverlay);
        toolbarStack.Children.Add(searchHost);

        var chipsRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var filterButtons = new Dictionary<FilterKind, Button>();
        var activeFilter = FilterKind.All;

        void AddFilterChip(FilterKind kind, string label)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 11.5,
                Padding = new Thickness(10, 3.5, 10, 3.5),
                Margin = new Thickness(0, 0, 6, 0)
            };
            filterButtons[kind] = btn;
            chipsRow.Children.Add(btn);
        }
        AddFilterChip(FilterKind.All, "全部");
        AddFilterChip(FilterKind.HighWeight, "高权重 (≥6)");
        AddFilterChip(FilterKind.Spelling, "拼写错误");
        AddFilterChip(FilterKind.Sentence, "例句填空");
        AddFilterChip(FilterKind.Flashcard, "卡片忘记");
        toolbarStack.Children.Add(chipsRow);

        toolbarCard.Child = toolbarStack;
        Grid.SetRow(toolbarCard, 1);
        root.Children.Add(toolbarCard);

        // 2. Scrollable list
        var listPanel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var scroll = new ScrollViewer
        {
            Content = listPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        // 3. Footer status
        var statusText = InputDialog.Text("点击单词可跳转查词；点击右上角「随机抽测」或「拼写抽测」开始攻克错题。", status: true);
        statusText.FontSize = 11.5;
        statusText.Margin = new Thickness(2, 10, 2, 0);
        Grid.SetRow(statusText, 3);
        root.Children.Add(statusText);

        InputDialog.SetBody(dialog, root);

        IReadOnlyList<MistakeBookItem> currentFiltered = [];

        static (Brush Bg, Brush Border, Brush Fg) WeightColors(int weight, bool isDark) => weight >= 6
            ? (ThemeManager.Hex(isDark ? "#401922" : "#FFEBEF"), ThemeManager.Hex(isDark ? "#9E3548" : "#F7A8B6"), ThemeManager.Hex(isDark ? "#FDA4AF" : "#B91C3B"))
            : weight >= 3
                ? (ThemeManager.Hex(isDark ? "#3B2814" : "#FFF6E5"), ThemeManager.Hex(isDark ? "#87571C" : "#F2CC82"), ThemeManager.Hex(isDark ? "#FBBF5C" : "#A65406"))
                : (ThemeManager.Hex(isDark ? "#123447" : "#E6F6FF"), ThemeManager.Hex(isDark ? "#267099" : "#A8DCFA"), ThemeManager.Hex(isDark ? "#73D1FE" : "#0A76A8"));

        void Render()
        {
            var all = loadItems();
            var totalWeight = all.Sum(i => i.Weight);
            var highCount = all.Count(i => i.Weight >= 6);
            var spellCount = all.Count(i => i.LastReason == "拼写错误");
            var sentCount = all.Count(i => i.LastReason == "例句填空");
            var cardCount = all.Count(i => i.LastReason == "卡片忘记");

            summaryText.Text = all.Count == 0
                ? "当前没有待攻克错题，继续保持！"
                : highCount > 0
                    ? $"共 {all.Count} 词 · 总权重 {totalWeight} · 高权重（≥6）{highCount} 词"
                    : $"共 {all.Count} 词 · 总权重 {totalWeight}";

            filterButtons[FilterKind.All].Content = $"全部 ({all.Count})";
            filterButtons[FilterKind.HighWeight].Content = $"高权重 ({highCount})";
            filterButtons[FilterKind.Spelling].Content = $"拼写错误 ({spellCount})";
            filterButtons[FilterKind.Sentence].Content = $"例句填空 ({sentCount})";
            filterButtons[FilterKind.Flashcard].Content = $"卡片忘记 ({cardCount})";

            foreach (var (kind, btn) in filterButtons)
            {
                var selected = kind == activeFilter;
                btn.SetResourceReference(Control.BackgroundProperty, selected ? "SelectedBgBrush" : "SurfaceBrush");
                btn.SetResourceReference(Control.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
                btn.SetResourceReference(Control.ForegroundProperty, selected ? "AccentDeepBrush" : "MutedBrush");
                btn.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }

            var query = searchBox.Text.Trim();
            searchOverlay.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            IEnumerable<MistakeBookItem> filtered = activeFilter switch
            {
                FilterKind.HighWeight => all.Where(i => i.Weight >= 6),
                FilterKind.Spelling => all.Where(i => i.LastReason == "拼写错误"),
                FilterKind.Sentence => all.Where(i => i.LastReason == "例句填空"),
                FilterKind.Flashcard => all.Where(i => i.LastReason == "卡片忘记"),
                _ => all
            };

            if (query.Length > 0)
            {
                filtered = filtered.Where(i =>
                {
                    if (i.Word.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
                    if (i.LastReason.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
                    if (i.LastWrongInput != null && i.LastWrongInput.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
                    var meaning = lookupEntry(i.Word)?.DisplayMeaning ?? "";
                    return meaning.Contains(query, StringComparison.OrdinalIgnoreCase);
                });
            }

            currentFiltered = filtered.ToArray();
            var hasFiltered = currentFiltered.Count > 0;
            randomQuizBtn.IsEnabled = hasFiltered;
            spellQuizBtn.IsEnabled = hasFiltered;
            randomQuizBtn.Content = hasFiltered ? $"随机抽测 ({currentFiltered.Count})" : "随机抽测";
            spellQuizBtn.Content = hasFiltered ? $"拼写抽测 ({currentFiltered.Count})" : "拼写抽测";
            if (cloudAddBtn != null) cloudAddBtn.IsEnabled = hasFiltered;

            listPanel.Children.Clear();
            if (!hasFiltered)
            {
                var emptyBox = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(24, 38, 24, 38),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                emptyBox.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
                emptyBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var emptyTitle = new TextBlock
                {
                    Text = all.Count == 0 ? "错题本已清空" : "没有符合当前筛选条件的错题",
                    FontSize = 15.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                emptyTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                var emptySub = new TextBlock
                {
                    Text = all.Count == 0
                        ? "在卡片自测、拼写默写、例句填空或同根辨析中答错的词，会自动收录并按权重安排抽测。"
                        : "可以清空搜索关键词或切换筛选标签查看其他错题。",
                    FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                emptySub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                emptyStack.Children.Add(emptyTitle);
                emptyStack.Children.Add(emptySub);
                emptyBox.Child = emptyStack;
                listPanel.Children.Add(emptyBox);
                return;
            }

            var isDark = ThemeManager.IsDark;
            foreach (var item in currentFiltered)
            {
                var captured = item;
                var entry = lookupEntry(captured.Word);
                var meaning = (entry?.DisplayMeaning ?? "").Replace("\r\n", "\n").Replace('\n', '；');
                if (meaning.Length > 92) meaning = meaning[..90].TrimEnd('；', '，', ' ') + "…";
                var phonetic = string.IsNullOrWhiteSpace(entry?.phonetic) ? "" : "/" + entry.phonetic.Trim().Trim('/') + "/";

                var cardStack = new StackPanel();
                var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };

                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(actions, Dock.Right);

                var speakBtn = new Button
                {
                    Content = "朗读",
                    FontSize = 11,
                    Padding = new Thickness(9, 2.5, 9, 2.5),
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "播放美式发音"
                };
                if (pillStyle != null) speakBtn.Style = pillStyle;
                speakBtn.Click += async (_, _) => await speakWord(captured.Word, EnglishAccent.American);

                var removeBtn = new Button
                {
                    Content = "移出",
                    FontSize = 11,
                    Padding = new Thickness(8, 2.5, 8, 2.5),
                    Margin = new Thickness(4, 0, 0, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    ToolTip = "手动移出错题本"
                };
                removeBtn.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
                removeBtn.Click += (_, _) =>
                {
                    if (removeWord(captured.Word))
                    {
                        statusText.Text = $"已将「{captured.Word}」移出错题本。";
                        Render();
                    }
                };

                actions.Children.Add(speakBtn);
                actions.Children.Add(removeBtn);
                topDock.Children.Add(actions);

                var leftRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

                var (wBg, wBd, wFg) = WeightColors(captured.Weight, isDark);
                var dots = captured.Weight <= 6 ? " " + new string('●', captured.Weight) : "";
                var weightBadge = new Border
                {
                    Background = wBg,
                    BorderBrush = wBd,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"当前权重 {captured.Weight}：再连续答对 {captured.Weight} 次将自动移出错题本；答错一次权重 +3",
                    Child = new TextBlock { Text = $"权重 {captured.Weight}{dots}", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = wFg }
                };
                leftRow.Children.Add(weightBadge);

                var wordBtn = new Button
                {
                    Content = captured.Word,
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(2, 0, 6, 0),
                    Margin = new Thickness(0, 0, 4, 2),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"点击查词「{captured.Word}」"
                };
                wordBtn.SetResourceReference(Control.ForegroundProperty, "AccentDeepBrush");
                wordBtn.Click += (_, _) => { dialog.Close(); lookupWord(captured.Word); };
                leftRow.Children.Add(wordBtn);

                if (phonetic.Length > 0)
                {
                    var ipaTb = new TextBlock
                    {
                        Text = phonetic,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 11.5,
                        Margin = new Thickness(0, 0, 7, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ipaTb.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                    leftRow.Children.Add(ipaTb);
                }

                var reasonBadge = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 1.5, 7, 1.5),
                    Margin = new Thickness(0, 0, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = $"{captured.LastReason} · 错 {captured.WrongCount} 次", FontSize = 10.5 }
                };
                reasonBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                reasonBadge.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                ((TextBlock)reasonBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                leftRow.Children.Add(reasonBadge);

                if (!string.IsNullOrWhiteSpace(captured.LastWrongInput))
                {
                    var wrongLabel = captured.LastReason == "拼写错误"
                        ? $"上次误拼：{captured.LastWrongInput}"
                        : $"上次误选：{captured.LastWrongInput}";
                    var wrongTb = new TextBlock
                    {
                        Text = wrongLabel,
                        FontSize = 11,
                        Margin = new Thickness(0, 0, 0, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = ThemeManager.Hex(isDark ? "#FDA4AF" : "#B91C3B")
                    };
                    leftRow.Children.Add(wrongTb);
                }

                topDock.Children.Add(leftRow);
                cardStack.Children.Add(topDock);

                if (meaning.Length > 0)
                {
                    var meanTb = new TextBlock
                    {
                        Text = meaning,
                        FontSize = 12.5,
                        LineHeight = 19,
                        TextWrapping = TextWrapping.Wrap
                    };
                    meanTb.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
                    cardStack.Children.Add(meanTb);
                }

                if (!string.IsNullOrWhiteSpace(captured.ContextSentence))
                {
                    var ctxTb = new TextBlock
                    {
                        Text = "例句：" + captured.ContextSentence,
                        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                        FontSize = 11.5,
                        LineHeight = 18,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    ctxTb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    cardStack.Children.Add(ctxTb);
                }

                var cardBorder = new Border
                {
                    Child = cardStack,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(14, 10, 14, 11),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                cardBorder.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
                cardBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                listPanel.Children.Add(cardBorder);
            }
        }

        foreach (var (kind, btn) in filterButtons)
        {
            var capturedKind = kind;
            btn.Click += (_, _) => { activeFilter = capturedKind; Render(); };
        }
        searchBox.TextChanged += (_, _) => Render();

        randomQuizBtn.Click += (_, _) =>
        {
            if (currentFiltered.Count == 0) return;
            startCardQuiz(dialog, currentFiltered.Select(i => i.Word).ToArray(), false);
            Render();
        };
        spellQuizBtn.Click += (_, _) =>
        {
            if (currentFiltered.Count == 0) return;
            startCardQuiz(dialog, currentFiltered.Select(i => i.Word).ToArray(), true);
            Render();
        };

        if (cloudAddBtn != null && addToCloudNotepad != null)
        {
            cloudAddBtn.Click += async (_, _) =>
            {
                if (currentFiltered.Count == 0) return;
                var words = currentFiltered.Select(i => i.Word).ToArray();
                cloudAddBtn.IsEnabled = false;
                statusText.Text = $"正在将 {words.Length} 个错题加入云词本…";
                try
                {
                    statusText.Text = await addToCloudNotepad(words);
                }
                catch (Exception ex)
                {
                    statusText.Text = "加入云词本失败：" + ex.Message;
                }
                finally
                {
                    cloudAddBtn.IsEnabled = currentFiltered.Count > 0;
                }
            };
        }

        Render();
        return dialog;
    }
}
