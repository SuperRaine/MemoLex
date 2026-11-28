using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal static class LearningDashboardDialog
{
    internal static Window Build(
        Window owner,
        Preferences preferences,
        FlashcardProgressSnapshot? stubbornProgress,
        int stubbornTotal,
        Action<string> lookupWord,
        ContextSentenceStore? contextSentences = null,
        SpellingMistakeStore? spellingMistakes = null,
        Action? startContextReview = null,
        string? accountKey = null)
    {
        var dialog = InputDialog.Shell(owner, "学习统计看板", out var body);
        dialog.Width = 660;
        dialog.MaxHeight = Math.Max(540, SystemParameters.WorkArea.Height - 60);

        var title = new TextBlock { Text = "学习统计看板", FontSize = 22, FontWeight = FontWeights.Bold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        body.Children.Add(title);

        var sub = InputDialog.Text("统计本机查词与自测记录，点击单词可直接复习。");
        sub.FontSize = 12;
        sub.Margin = new Thickness(0, 3, 0, 14);
        body.Children.Add(sub);

        var history = preferences.SearchHistory;
        var today = DateTime.Today;
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(-6 + i)).ToArray();
        var dailyCounts = days.Select(day => history.Count(h => h.LastSearched?.ToLocalTime().Date == day)).ToArray();
        var weekTotal = dailyCounts.Sum();
        var totalLookups = history.Sum(h => (long)h.LookupCount);
        var repeatedCount = history.Count(h => h.LookupCount >= 3);
        var masteredCount = stubbornProgress == null ? 0 : Math.Max(0, stubbornProgress.Cursor - (stubbornProgress.Queue?.Length ?? 0));

        // 4 KPI stat cards
        var kpiGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var i = 0; i < 4; i++) kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddKpi(int col, string label, string value, string hint, string accentHex)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(13, 11, 13, 11),
                Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 3 ? 0 : 4, 0)
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var stack = new StackPanel();
            var lbl = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold };
            lbl.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var val = new TextBlock { Text = value, FontSize = 23, FontWeight = FontWeights.Bold, Foreground = ThemeManager.Hex(accentHex), Margin = new Thickness(0, 3, 0, 2) };
            var ht = new TextBlock { Text = hint, FontSize = 10.5 };
            ht.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
            stack.Children.Add(lbl);
            stack.Children.Add(val);
            stack.Children.Add(ht);
            card.Child = stack;
            Grid.SetColumn(card, col);
            kpiGrid.Children.Add(card);
        }

        AddKpi(0, "近 7 天查词", $"{weekTotal}", "查过的词", "#0EA5E9");
        AddKpi(1, "累计查词", $"{totalLookups:N0}", $"共 {history.Count} 个不同词", "#14B8A6");
        AddKpi(2, "反复查词", $"{repeatedCount}", "查询 ≥ 3 次", "#D97706");
        AddKpi(3, "卡片已掌握", $"{masteredCount}", stubbornTotal > 0 ? $"顽固词共 {stubbornTotal}" : "来自卡片自测", "#10B981");
        body.Children.Add(kpiGrid);

        // 7-day bar chart
        var chartCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(0, 0, 0, 14)
        };
        chartCard.SetResourceReference(Border.BackgroundProperty, "CardAltBrush");
        chartCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var chartStack = new StackPanel();
        var chartTitle = new TextBlock { Text = "近 7 天查词趋势", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        chartTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        chartStack.Children.Add(chartTitle);

        var maxVal = Math.Max(1, dailyCounts.Max());
        var barsGrid = new Grid { Height = 106 };
        for (var i = 0; i < 7; i++) barsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < 7; i++)
        {
            var count = dailyCounts[i];
            var barHeight = count == 0 ? 4.0 : Math.Max(12.0, 68.0 * count / maxVal);
            var colStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
            var numText = new TextBlock { Text = count > 0 ? count.ToString() : "", FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            numText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            var bar = new Border
            {
                Width = 26,
                Height = barHeight,
                CornerRadius = new CornerRadius(5, 5, 2, 2),
                Background = count > 0
                    ? new LinearGradientBrush(Color.FromRgb(44, 184, 248), Color.FromRgb(13, 136, 200), new Point(0, 0), new Point(0, 1))
                    : ThemeManager.Res("BorderBrush", "#D9E8F4", "#2C4052")
            };
            var dayLabel = new TextBlock
            {
                Text = i == 6 ? "今天" : days[i].ToString("MM/dd"),
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            dayLabel.SetResourceReference(TextBlock.ForegroundProperty, i == 6 ? "InkStrongBrush" : "MutedBrush");
            colStack.Children.Add(numText);
            colStack.Children.Add(bar);
            colStack.Children.Add(dayLabel);
            Grid.SetColumn(colStack, i);
            barsGrid.Children.Add(colStack);
        }
        chartStack.Children.Add(barsGrid);
        chartCard.Child = chartStack;
        body.Children.Add(chartCard);

        // Top repeated words
        var topCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(0, 0, 0, 12)
        };
        topCard.SetResourceReference(Border.BackgroundProperty, "CardAltBrush");
        topCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var topStack = new StackPanel();
        var topTitle = new TextBlock { Text = "最常复查的词", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        topTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        topStack.Children.Add(topTitle);

        var topWords = history
            .Where(h => h.LookupCount >= 2)
            .OrderByDescending(h => h.LookupCount)
            .ThenByDescending(h => h.LastSearched)
            .Take(5)
            .ToArray();

        if (topWords.Length == 0)
        {
            var empty = InputDialog.Text("暂无重复查询 2 次以上的单词，继续查词积累吧！");
            empty.FontSize = 12;
            topStack.Children.Add(empty);
        }
        else
        {
            var pillStyle = Application.Current.TryFindResource("PillButton") as Style;
            var wrap = new WrapPanel();
            foreach (var item in topWords)
            {
                var word = item.Word;
                var btn = new Button
                {
                    Content = $"{word}  ·  {item.LookupCount} 次",
                    FontSize = 12,
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 8, 6),
                    ToolTip = $"点击复习「{word}」（最近查询：{item.DisplayTime}）"
                };
                if (pillStyle != null) btn.Style = pillStyle;
                btn.Click += (_, _) =>
                {
                    dialog.Close();
                    lookupWord(word);
                };
                wrap.Children.Add(btn);
            }
            topStack.Children.Add(wrap);
        }
        topCard.Child = topStack;
        body.Children.Add(topCard);

        // Saved context sentences & active spelling mistakes card
        if (contextSentences != null || spellingMistakes != null)
        {
            var savedSentences = contextSentences?.List(null, accountKey) ?? [];
            var activeMistakes = spellingMistakes?.ListActive() ?? [];
            var practiceCard = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 13, 16, 13),
                Margin = new Thickness(0, 0, 0, 12)
            };
            practiceCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
            practiceCard.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
            var practiceDock = new DockPanel();

            if (startContextReview != null)
            {
                var reviewBtn = new Button
                {
                    Content = savedSentences.Count > 0 ? $"例句自测（{savedSentences.Count}）" : "暂无例句",
                    IsEnabled = savedSentences.Count > 0,
                    FontSize = 12,
                    Padding = new Thickness(14, 7, 14, 7),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    ToolTip = "用收藏的例句进行卡片自测"
                };
                if (Application.Current.TryFindResource("AccentButton") is Style accentStyle) reviewBtn.Style = accentStyle;
                reviewBtn.Click += (_, _) =>
                {
                    dialog.Close();
                    startContextReview();
                };
                DockPanel.SetDock(reviewBtn, Dock.Right);
                practiceDock.Children.Add(reviewBtn);
            }

            var practiceInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var practiceTitle = new TextBlock
            {
                Text = "例句自测与拼写巩固",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            practiceTitle.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
            practiceInfo.Children.Add(practiceTitle);

            var detailParts = new List<string>
            {
                $"已收藏 {savedSentences.Count} 条例句"
            };
            if (activeMistakes.Count > 0)
            {
                var sample = string.Join("、", activeMistakes.Take(3).Select(m => $"{m.Word}（{m.CorrectOption}/{m.WrongOption}）"));
                detailParts.Add($"待巩固拼写 {activeMistakes.Count} 词：{sample}");
            }
            else
            {
                detailParts.Add("拼写出错时会自动记下易错位置，连续拼对 2 次后自动清除");
            }
            var practiceDesc = new TextBlock
            {
                Text = string.Join(" · ", detailParts),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            practiceDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            practiceInfo.Children.Add(practiceDesc);
            practiceDock.Children.Add(practiceInfo);

            practiceCard.Child = practiceDock;
            body.Children.Add(practiceCard);
        }

        // Exam countdown card if enabled
        if (preferences.ShowExamCountdown && preferences.ExamDate is { } examDate)
        {
            var daysLeft = Math.Max(0, (examDate.Date - DateTime.Today).Days);
            var examCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Background = ThemeManager.Res("WarnBgBrush", "#FFF7E6", "#332614"),
                BorderBrush = ThemeManager.Res("WarnBorderBrush", "#F4CF86", "#6E4F1D")
            };
            var examRow = new DockPanel();
            var daysBadge = new TextBlock
            {
                Text = $"还剩 {daysLeft} 天",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeManager.Res("WarnFgBrush", "#9A5207", "#F8C76A"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(daysBadge, Dock.Right);
            examRow.Children.Add(daysBadge);
            examRow.Children.Add(new TextBlock
            {
                Text = $"目标考试：{preferences.ExamName}（{examDate:yyyy-MM-dd}）",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeManager.Res("WarnFgBrush", "#9A5207", "#F8C76A"),
                VerticalAlignment = VerticalAlignment.Center
            });
            examCard.Child = examRow;
            body.Children.Add(examCard);
        }

        return dialog;
    }
}
