using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal static class MistakeBookDialog
{
    private enum SubFilterKind { All, HighWeight }

    internal static Window Build(
        Window owner,
        Func<string?, IReadOnlyList<MistakeBookItem>> loadItems,
        Func<string, DictionaryEntry?> lookupEntry,
        Action<string> lookupWord,
        Func<string, EnglishAccent, Task> speakWord,
        Func<string, string, bool> removeWord,
        Action<Window, IReadOnlyList<string>> startSpellingQuiz,
        Func<Window, IReadOnlyList<MistakeBookItem>, Task<string?>> startSentenceQuiz,
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: category tabs + toolbar
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

        var practiceBtn = new Button
        {
            Content = "拼写抽测",
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        if (accentStyle != null) practiceBtn.Style = accentStyle;
        headerActions.Children.Add(practiceBtn);

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

        // 1. Category Switcher + Rule + Search + Sub-filter
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

        // Segmented book tabs: Spelling vs Sentence
        var tabBarBorder = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 0, 9),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        tabBarBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        tabBarBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var tabBar = new StackPanel { Orientation = Orientation.Horizontal };
        var spellingTabBtn = new Button
        {
            Content = "拼写错题",
            FontSize = 12.5,
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 0, 4, 0),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        var sentenceTabBtn = new Button
        {
            Content = "例句错题",
            FontSize = 12.5,
            Padding = new Thickness(14, 5, 14, 5),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        tabBar.Children.Add(spellingTabBtn);
        tabBar.Children.Add(sentenceTabBtn);
        tabBarBorder.Child = tabBar;
        toolbarStack.Children.Add(tabBarBorder);

        var ruleStrip = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(1, 0, 0, 8)
        };
        ruleStrip.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        toolbarStack.Children.Add(ruleStrip);

        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
        var chipsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(chipsRow, Dock.Right);

        var filterButtons = new Dictionary<SubFilterKind, Button>();
        var activeSubFilter = SubFilterKind.All;

        void AddSubFilterChip(SubFilterKind kind, string label)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 11.5,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 5, 0)
            };
            filterButtons[kind] = btn;
            chipsRow.Children.Add(btn);
        }
        AddSubFilterChip(SubFilterKind.All, "全部");
        AddSubFilterChip(SubFilterKind.HighWeight, "重点易错");
        searchRow.Children.Add(chipsRow);

        var searchHost = new Grid();
        var searchBox = new TextBox
        {
            Height = 32,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 2, 10, 2),
            ToolTip = "按单词、释义、误拼或例句筛选"
        };
        var searchOverlay = new TextBlock
        {
            Text = "筛选当前分类中的单词、释义或例句…",
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsHitTestVisible = false
        };
        searchOverlay.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        searchHost.Children.Add(searchBox);
        searchHost.Children.Add(searchOverlay);
        searchRow.Children.Add(searchHost);
        toolbarStack.Children.Add(searchRow);

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
        var statusText = InputDialog.Text("", status: true);
        statusText.FontSize = 11.5;
        statusText.Margin = new Thickness(2, 10, 2, 0);
        Grid.SetRow(statusText, 3);
        root.Children.Add(statusText);

        InputDialog.SetBody(dialog, root);

        var initialSpelling = loadItems(MistakeBookStore.CategorySpelling);
        var initialSentence = loadItems(MistakeBookStore.CategorySentence);
        var activeCategory = initialSpelling.Count == 0 && initialSentence.Count > 0
            ? MistakeBookStore.CategorySentence
            : MistakeBookStore.CategorySpelling;
        IReadOnlyList<MistakeBookItem> currentFiltered = [];
        var launchingQuiz = false;

        static (Brush Bg, Brush Border, Brush Fg) WeightColors(int weight, bool isDark) => weight >= 6
            ? (ThemeManager.Hex(isDark ? "#401922" : "#FFEBEF"), ThemeManager.Hex(isDark ? "#9E3548" : "#F7A8B6"), ThemeManager.Hex(isDark ? "#FDA4AF" : "#B91C3B"))
            : weight >= 3
                ? (ThemeManager.Hex(isDark ? "#3B2814" : "#FFF6E5"), ThemeManager.Hex(isDark ? "#87571C" : "#F2CC82"), ThemeManager.Hex(isDark ? "#FBBF5C" : "#A65406"))
                : (ThemeManager.Hex(isDark ? "#123447" : "#E6F6FF"), ThemeManager.Hex(isDark ? "#267099" : "#A8DCFA"), ThemeManager.Hex(isDark ? "#73D1FE" : "#0A76A8"));

        void StyleTabButton(Button btn, bool selected)
        {
            btn.SetResourceReference(Control.BackgroundProperty, selected ? "SelectedBgBrush" : "SurfaceBrush");
            btn.SetResourceReference(Control.BorderBrushProperty, selected ? "AccentBrush" : "SurfaceBrush");
            btn.SetResourceReference(Control.ForegroundProperty, selected ? "AccentDeepBrush" : "MutedBrush");
            btn.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
        }

        void Render(string? customStatus = null)
        {
            var spellingItems = loadItems(MistakeBookStore.CategorySpelling);
            var sentenceItems = loadItems(MistakeBookStore.CategorySentence);
            var isSentence = activeCategory == MistakeBookStore.CategorySentence;
            var bookItems = isSentence ? sentenceItems : spellingItems;
            var highCount = bookItems.Count(i => i.Weight >= 6);
            var totalCount = spellingItems.Count + sentenceItems.Count;

            summaryText.Text = totalCount == 0
                ? "仅收录拼写默写与例句选择题的错题，多次答对后自动移出"
                : $"拼写错题 {spellingItems.Count} 词 · 例句错题 {sentenceItems.Count} 题";

            spellingTabBtn.Content = $"拼写错题 ({spellingItems.Count})";
            sentenceTabBtn.Content = $"例句错题 ({sentenceItems.Count})";
            StyleTabButton(spellingTabBtn, !isSentence);
            StyleTabButton(sentenceTabBtn, isSentence);

            ruleStrip.Text = isSentence
                ? "例句四选一自测中答错的题目会自动收录到这里；点击「例句选择题抽测」继续做四选一选择题，多次答对后自动移出。"
                : "拼写默写中拼错的单词会自动收录到这里；点击「拼写抽测」集中默写，常错词优先出现，多次拼对后自动移出。";

            filterButtons[SubFilterKind.All].Content = $"全部 ({bookItems.Count})";
            filterButtons[SubFilterKind.HighWeight].Content = $"重点易错 ({highCount})";
            foreach (var (kind, btn) in filterButtons)
            {
                var selected = kind == activeSubFilter;
                btn.SetResourceReference(Control.BackgroundProperty, selected ? "SelectedBgBrush" : "SurfaceBrush");
                btn.SetResourceReference(Control.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
                btn.SetResourceReference(Control.ForegroundProperty, selected ? "AccentDeepBrush" : "MutedBrush");
                btn.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }

            var query = searchBox.Text.Trim();
            searchOverlay.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            IEnumerable<MistakeBookItem> filtered = activeSubFilter == SubFilterKind.HighWeight
                ? bookItems.Where(i => i.Weight >= 6)
                : bookItems;

            if (query.Length > 0)
            {
                filtered = filtered.Where(i =>
                {
                    if (i.Word.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
                    if (i.LastWrongInput != null && i.LastWrongInput.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
                    if (i.ContextSentence != null && i.ContextSentence.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
                    var meaning = lookupEntry(i.Word)?.DisplayMeaning ?? "";
                    return meaning.Contains(query, StringComparison.OrdinalIgnoreCase);
                });
            }

            currentFiltered = filtered.ToArray();
            var hasFiltered = currentFiltered.Count > 0;
            practiceBtn.IsEnabled = hasFiltered && !launchingQuiz;
            if (isSentence)
            {
                practiceBtn.Content = launchingQuiz
                    ? "正在出题…"
                    : hasFiltered ? $"例句选择题抽测 ({currentFiltered.Count})" : "例句选择题抽测";
                practiceBtn.ToolTip = "以四选一语境选择题形式抽测这些例句错题，答对自动巩固并逐步移出";
            }
            else
            {
                practiceBtn.Content = hasFiltered ? $"拼写抽测 ({currentFiltered.Count})" : "拼写抽测";
                practiceBtn.ToolTip = "直接进入拼写默写模式随机抽测这些拼写错题，常错词优先出现";
            }
            if (cloudAddBtn != null) cloudAddBtn.IsEnabled = hasFiltered;

            if (customStatus != null)
            {
                statusText.Text = customStatus;
            }
            else
            {
                statusText.Text = isSentence
                    ? "点击单词可跳转查词；点击右上角「例句选择题抽测」继续用四选一选择题巩固例句错题。"
                    : "点击单词可跳转查词；点击右上角「拼写抽测」集中默写巩固拼写错题。";
            }

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
                    Text = bookItems.Count == 0
                        ? (isSentence ? "暂无例句错题" : "暂无拼写错题")
                        : "没有符合当前筛选条件的错题",
                    FontSize = 15.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                emptyTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                var emptySub = new TextBlock
                {
                    Text = bookItems.Count == 0
                        ? (isSentence
                            ? "在「例句四选一自测」中选错的题目会自动收录到这里，方便后续继续做选择题巩固。"
                            : "在「拼写默写」中输入有效英文但拼错的单词，会自动收录到这里方便集中抽测。")
                        : "可以清空搜索关键词或切换「全部」查看该分类下的其他错题。",
                    FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
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
                    ToolTip = isSentence ? "从例句错题中移出" : "从拼写错题中移出"
                };
                removeBtn.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
                removeBtn.Click += (_, _) =>
                {
                    if (removeWord(captured.Word, captured.Category ?? activeCategory))
                        Render($"已将「{captured.Word}」移出{(isSentence ? "例句错题" : "拼写错题")}。");
                };

                actions.Children.Add(speakBtn);
                actions.Children.Add(removeBtn);
                topDock.Children.Add(actions);

                var leftRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

                var (wBg, wBd, wFg) = WeightColors(captured.Weight, isDark);
                var levelLabel = MistakeBookStore.LevelLabel(captured.Weight);
                var levelTip = captured.Weight >= 6
                    ? "多次答错，抽测时会优先出现，多次答对后将自动移出"
                    : captured.Weight >= 3
                        ? "抽测时会优先巩固，连续答对后将自动移出"
                        : "已经快掌握了，再答对几次即可自动移出";
                var levelBadge = new Border
                {
                    Background = wBg,
                    BorderBrush = wBd,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = levelTip,
                    Child = new TextBlock { Text = levelLabel, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = wFg }
                };
                leftRow.Children.Add(levelBadge);

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

                var statsText = captured.RightCount > 0
                    ? $"错 {captured.WrongCount} · 对 {captured.RightCount}"
                    : $"错 {captured.WrongCount} 次";
                var statBadge = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 1.5, 7, 1.5),
                    Margin = new Thickness(0, 0, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = statsText, FontSize = 10.5 }
                };
                statBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                statBadge.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                ((TextBlock)statBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                leftRow.Children.Add(statBadge);

                if (!string.IsNullOrWhiteSpace(captured.LastWrongInput))
                {
                    var wrongLabel = isSentence
                        ? $"上次误选：{captured.LastWrongInput}"
                        : $"上次误拼：{captured.LastWrongInput}";
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

                if (isSentence && !string.IsNullOrWhiteSpace(captured.ContextSentence))
                {
                    var ctxBox = new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(10, 6, 10, 7),
                        Margin = new Thickness(0, 6, 0, 0)
                    };
                    ctxBox.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                    ctxBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                    var ctxStack = new StackPanel();
                    var ctxTb = new TextBlock
                    {
                        Text = captured.ContextSentence,
                        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                        FontSize = 12,
                        LineHeight = 18,
                        TextWrapping = TextWrapping.Wrap
                    };
                    ctxTb.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                    ctxStack.Children.Add(ctxTb);
                    if (!string.IsNullOrWhiteSpace(captured.ContextTranslation))
                    {
                        var zhTb = new TextBlock
                        {
                            Text = captured.ContextTranslation,
                            FontSize = 11.5,
                            LineHeight = 17,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        zhTb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                        ctxStack.Children.Add(zhTb);
                    }
                    ctxBox.Child = ctxStack;
                    cardStack.Children.Add(ctxBox);
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

        spellingTabBtn.Click += (_, _) =>
        {
            if (activeCategory == MistakeBookStore.CategorySpelling) return;
            activeCategory = MistakeBookStore.CategorySpelling;
            activeSubFilter = SubFilterKind.All;
            Render();
        };
        sentenceTabBtn.Click += (_, _) =>
        {
            if (activeCategory == MistakeBookStore.CategorySentence) return;
            activeCategory = MistakeBookStore.CategorySentence;
            activeSubFilter = SubFilterKind.All;
            Render();
        };

        foreach (var (kind, btn) in filterButtons)
        {
            var capturedKind = kind;
            btn.Click += (_, _) => { activeSubFilter = capturedKind; Render(); };
        }
        searchBox.TextChanged += (_, _) => Render();

        practiceBtn.Click += async (_, _) =>
        {
            if (currentFiltered.Count == 0 || launchingQuiz) return;
            if (activeCategory == MistakeBookStore.CategorySentence)
            {
                launchingQuiz = true;
                Render("正在生成例句四选一选择题…");
                try
                {
                    var notice = await startSentenceQuiz(dialog, currentFiltered);
                    launchingQuiz = false;
                    Render(notice);
                }
                catch (Exception ex)
                {
                    launchingQuiz = false;
                    Render("生成例句选择题失败：" + ex.Message);
                }
            }
            else
            {
                startSpellingQuiz(dialog, currentFiltered.Select(i => i.Word).ToArray());
                Render();
            }
        };

        if (cloudAddBtn != null && addToCloudNotepad != null)
        {
            cloudAddBtn.Click += async (_, _) =>
            {
                if (currentFiltered.Count == 0) return;
                var words = currentFiltered.Select(i => i.Word).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
