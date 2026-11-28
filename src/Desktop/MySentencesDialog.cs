using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal static class MySentencesDialog
{
    private enum FilterKind { All, Cloud, Local, Unreviewed }

    internal static Window Build(
        Window owner,
        Func<IReadOnlyList<ContextSentenceItem>> loadItems,
        Action<string> lookupWord,
        Func<Button, string, Task> speakSentence,
        Action<Window, ContextSentenceItem?> openEditor,
        Func<Window, ContextSentenceItem, Task<bool>> deleteItem,
        Func<Window, IReadOnlyList<ContextSentenceItem>, Task> startReview)
    {
        var dialog = InputDialog.Shell(owner, "我的例句", out _);
        dialog.Width = 780;
        dialog.Height = Math.Min(680, Math.Max(540, SystemParameters.WorkArea.Height - 60));
        dialog.MinWidth = 620;
        dialog.MinHeight = 480;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.ResizeMode = ResizeMode.CanResize;

        var root = new Grid { Margin = new Thickness(22, 16, 22, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: search + filters + blank toggle
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: scrollable list
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: footer hint

        var pillStyle = Application.Current?.TryFindResource("PillButton") as Style;
        var accentStyle = Application.Current?.TryFindResource("AccentButton") as Style;

        // 0. Header
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var addBtn = new Button
        {
            Content = "＋ 添加例句",
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "新建自定义例句，支持保存到本地或同步到墨墨云端"
        };
        if (pillStyle != null) addBtn.Style = pillStyle;

        var startReviewBtn = new Button
        {
            Content = "开始自测",
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "使用当前列表中的例句进行四选一语境填空自测（含近义词/形近词/派生词混淆与解析）"
        };
        if (accentStyle != null) startReviewBtn.Style = accentStyle;

        headerActions.Children.Add(addBtn);
        headerActions.Children.Add(startReviewBtn);
        DockPanel.SetDock(headerActions, Dock.Right);
        headerDock.Children.Add(headerActions);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock { Text = "我的例句", FontSize = 21, FontWeight = FontWeights.Bold };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        var summaryText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
        summaryText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        titleStack.Children.Add(titleBlock);
        titleStack.Children.Add(summaryText);
        headerDock.Children.Add(titleStack);
        root.Children.Add(headerDock);

        // 1. Search + filter toolbar
        var filterCard = new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        filterCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        filterCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var filterStack = new StackPanel();
        var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        var searchBox = new TextBox
        {
            Height = 33,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 2, 10, 2),
            ToolTip = "按单词、例句、译文、来源或标签筛选"
        };
        var searchOverlay = new TextBlock
        {
            Text = "筛选单词、例句、译文、来源或标签…",
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsHitTestVisible = false
        };
        searchOverlay.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        searchHost.Children.Add(searchBox);
        searchHost.Children.Add(searchOverlay);
        filterStack.Children.Add(searchHost);

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
        AddFilterChip(FilterKind.Cloud, "云端");
        AddFilterChip(FilterKind.Local, "本地");
        AddFilterChip(FilterKind.Unreviewed, "未自测");

        filterStack.Children.Add(chipsRow);
        filterCard.Child = filterStack;
        Grid.SetRow(filterCard, 1);
        root.Children.Add(filterCard);

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
        var footerHint = InputDialog.Text("点击单词可跳转查词；点击右上角「开始自测」可进行四选一语境填空练习（含近义词、形近词、派生词混淆与详细解析）。", status: true);
        footerHint.FontSize = 11.5;
        footerHint.Margin = new Thickness(2, 10, 2, 0);
        Grid.SetRow(footerHint, 3);
        root.Children.Add(footerHint);

        InputDialog.SetBody(dialog, root);

        IReadOnlyList<ContextSentenceItem> currentFiltered = [];

        void StyleFilterChips(int allCount, int cloudCount, int localCount, int unreviewedCount)
        {
            filterButtons[FilterKind.All].Content = $"全部 ({allCount})";
            filterButtons[FilterKind.Cloud].Content = $"云端 ({cloudCount})";
            filterButtons[FilterKind.Local].Content = $"本地 ({localCount})";
            filterButtons[FilterKind.Unreviewed].Content = $"未自测 ({unreviewedCount})";

            foreach (var (kind, btn) in filterButtons)
            {
                var selected = kind == activeFilter;
                btn.SetResourceReference(Control.BackgroundProperty, selected ? "SelectedBgBrush" : "SurfaceBrush");
                btn.SetResourceReference(Control.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
                btn.SetResourceReference(Control.ForegroundProperty, selected ? "AccentDeepBrush" : "MutedBrush");
                btn.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        TextBlock BuildSentenceBlock(ContextSentenceItem item)
        {
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                FontSize = 14.5,
                LineHeight = 23,
                TextWrapping = TextWrapping.Wrap
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");

            var matchSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ExampleText.NormalizeLookup(item.Word) };
            if (!string.IsNullOrWhiteSpace(item.MatchedForm))
                matchSet.Add(ExampleText.NormalizeLookup(item.MatchedForm));

            foreach (var token in ExampleText.Tokenize(item.Sentence))
            {
                var run = new Run(token.Text);
                if (token.LookupWord != null && matchSet.Contains(token.LookupWord))
                {
                    run.FontWeight = FontWeights.Bold;
                    run.SetResourceReference(TextElement.ForegroundProperty, "AccentDeepBrush");
                    run.SetResourceReference(TextElement.BackgroundProperty, "PillBgBrush");
                }
                tb.Inlines.Add(run);
            }
            return tb;
        }

        void Render()
        {
            var all = loadItems();
            var cloudCount = all.Count(x => x.IsCloud);
            var localCount = all.Count - cloudCount;
            var unreviewedCount = all.Count(x => x.ReviewCount == 0);
            var distinctWords = all.Select(x => x.Word.Trim()).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            summaryText.Text = all.Count == 0
                ? "在真题例句、段落翻译中点击「收藏例句」，或直接在此「添加例句」"
                : cloudCount > 0
                    ? $"共 {all.Count} 条例句 · {distinctWords} 个单词 · 云端 {cloudCount} 条"
                    : $"共 {all.Count} 条例句 · {distinctWords} 个单词";

            StyleFilterChips(all.Count, cloudCount, localCount, unreviewedCount);

            var query = searchBox.Text.Trim();
            searchOverlay.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            IEnumerable<ContextSentenceItem> filtered = all;
            filtered = activeFilter switch
            {
                FilterKind.Cloud => filtered.Where(x => x.IsCloud),
                FilterKind.Local => filtered.Where(x => !x.IsCloud),
                FilterKind.Unreviewed => filtered.Where(x => x.ReviewCount == 0),
                _ => filtered
            };

            if (query.Length > 0)
            {
                filtered = filtered.Where(x =>
                    x.Word.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Sentence.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Translation.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (x.Tags != null && x.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))));
            }

            currentFiltered = filtered.ToArray();
            var isFiltered = activeFilter != FilterKind.All || query.Length > 0;
            startReviewBtn.IsEnabled = currentFiltered.Count > 0;
            startReviewBtn.Content = currentFiltered.Count == 0
                ? "开始自测"
                : isFiltered
                    ? $"自测当前 ({currentFiltered.Count})"
                    : $"开始自测 ({currentFiltered.Count})";

            listPanel.Children.Clear();
            if (currentFiltered.Count == 0)
            {
                var emptyBox = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(24, 34, 24, 34),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                emptyBox.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
                emptyBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var emptyTitle = new TextBlock
                {
                    Text = all.Count == 0 ? "还没有收藏或自建例句" : "没有符合当前筛选条件的例句",
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                emptyTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                var emptySub = new TextBlock
                {
                    Text = all.Count == 0
                        ? "点击右上角「＋ 添加例句」开始记录，或在真题例句、翻译中一键收藏。"
                        : "可以清空搜索关键词或切换筛选标签查看其他例句。",
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

            foreach (var item in currentFiltered)
            {
                var captured = item;

                var cardStack = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };

                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(actions, Dock.Right);

                var speakBtn = new Button
                {
                    Content = "朗读",
                    FontSize = 11,
                    Padding = new Thickness(9, 2.5, 9, 2.5),
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "朗读例句"
                };
                if (pillStyle != null) speakBtn.Style = pillStyle;
                speakBtn.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await speakSentence(speakBtn, captured.Sentence);
                };

                var editBtn = new Button
                {
                    Content = "编辑",
                    FontSize = 11,
                    Padding = new Thickness(8, 2.5, 8, 2.5),
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    ToolTip = "编辑例句内容或同步到墨墨云端"
                };
                editBtn.SetResourceReference(Control.ForegroundProperty, "AccentDeepBrush");
                editBtn.Click += (_, e) =>
                {
                    e.Handled = true;
                    openEditor(dialog, captured);
                    Render();
                };

                var delBtn = new Button
                {
                    Content = "删除",
                    FontSize = 11,
                    Padding = new Thickness(7, 2.5, 7, 2.5),
                    Margin = new Thickness(2, 0, 0, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    ToolTip = captured.IsCloud ? "删除这条云端例句（同步移除云端与本机）" : "删除这条本地例句"
                };
                delBtn.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
                delBtn.Click += async (_, e) =>
                {
                    e.Handled = true;
                    delBtn.IsEnabled = false;
                    try
                    {
                        if (await deleteItem(dialog, captured)) Render();
                    }
                    finally { delBtn.IsEnabled = true; }
                };

                actions.Children.Add(speakBtn);
                actions.Children.Add(editBtn);
                actions.Children.Add(delBtn);
                head.Children.Add(actions);

                var badges = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

                var wordBtn = new Button
                {
                    Content = captured.Word,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(9, 2.5, 9, 2.5),
                    Margin = new Thickness(0, 0, 6, 2),
                    ToolTip = $"点击跳转到「{captured.Word}」词条页"
                };
                if (pillStyle != null) wordBtn.Style = pillStyle;
                wordBtn.Click += (_, e) =>
                {
                    e.Handled = true;
                    dialog.Close();
                    lookupWord(captured.Word);
                };
                badges.Children.Add(wordBtn);

                var originLabel = captured.IsCloud ? "云端" : captured.HasUnsyncedCloudEdit ? "待同步" : "本地";
                var originBadge = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 1.5, 7, 1.5),
                    Margin = new Thickness(0, 0, 5, 2),
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

                var metaText = captured.ReviewCount > 0
                    ? $"{captured.Source} · 已自测 {captured.ReviewCount} 次"
                    : captured.Source;
                var metaBadge = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 1.5, 7, 1.5),
                    Margin = new Thickness(0, 0, 5, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = metaText, FontSize = 10.5 }
                };
                metaBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                metaBadge.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                ((TextBlock)metaBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                badges.Children.Add(metaBadge);

                if (captured.Tags is { Count: > 0 })
                {
                    foreach (var tag in captured.Tags.Take(3))
                    {
                        var tagBadge = new Border
                        {
                            CornerRadius = new CornerRadius(8),
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(6, 1.5, 6, 1.5),
                            Margin = new Thickness(0, 0, 4, 2),
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock { Text = tag, FontSize = 10 }
                        };
                        tagBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                        tagBadge.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        ((TextBlock)tagBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                        badges.Children.Add(tagBadge);
                    }
                }

                head.Children.Add(badges);
                cardStack.Children.Add(head);
                cardStack.Children.Add(BuildSentenceBlock(captured));

                if (!string.IsNullOrWhiteSpace(captured.Translation))
                {
                    var zh = new TextBlock
                    {
                        Text = captured.Translation,
                        FontSize = 12.5,
                        LineHeight = 20,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 0)
                    };
                    zh.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    cardStack.Children.Add(zh);
                }

                var cardBorder = new Border
                {
                    Child = cardStack,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(14, 11, 14, 12),
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
            btn.Click += (_, _) =>
            {
                activeFilter = capturedKind;
                Render();
            };
        }

        searchBox.TextChanged += (_, _) => Render();

        addBtn.Click += (_, _) =>
        {
            openEditor(dialog, null);
            Render();
        };

        startReviewBtn.Click += async (_, _) =>
        {
            if (currentFiltered.Count == 0) return;
            var deck = currentFiltered;
            startReviewBtn.IsEnabled = false;
            var previousLabel = startReviewBtn.Content;
            startReviewBtn.Content = "正在出题…";
            try
            {
                await startReview(dialog, deck);
            }
            finally
            {
                startReviewBtn.Content = previousLabel;
                Render();
            }
        };

        Render();
        return dialog;
    }
}
