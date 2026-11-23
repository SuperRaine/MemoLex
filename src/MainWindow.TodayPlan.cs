using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private void TodayPlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowCloud) { StatusText.Text = "联网后可查看今日学习"; return; }
        if (!_tokens.HasToken) { EditSettings("墨墨账号"); return; }

        using var session = CancellationTokenSource.CreateLinkedTokenSource(_accountCts.Token);
        var cancellation = session.Token;
        var dialog = InputDialog.Shell(this, "今日学习", out _);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.ResizeMode = ResizeMode.CanResizeWithGrip;
        dialog.Width = Math.Min(820, SystemParameters.WorkArea.Width - 30);
        dialog.Height = Math.Min(665, SystemParameters.WorkArea.Height - 30);
        dialog.MinWidth = 570; dialog.MinHeight = 495;
        using var accountChanged = cancellation.Register(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (dialog.IsVisible) dialog.Close();
        })));
        var body = BuildRows(new Thickness(20, 14, 20, 13), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        InputDialog.SetBody(dialog, body);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var refresh = new Button { Content = "刷新", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(refresh, Dock.Right); header.Children.Add(refresh);
        var overview = InputDialog.Text("正在读取今日学习…", status: true);
        overview.FontSize = 14; overview.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(overview); body.Children.Add(header);
        var tabs = new TabControl(); Grid.SetRow(tabs, 1); body.Children.Add(tabs);
        var status = InputDialog.Text("", status: true); status.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(status, 2); body.Children.Add(status);

        var duePanel = BuildRows(new Thickness(12), GridLength.Auto, new GridLength(1, GridUnitType.Star));
        tabs.Items.Add(new TabItem { Header = "待学词汇", Content = duePanel });
        var dueFilter = new ComboBox { ItemsSource = new[] { "全部待学", "待新学", "待复习" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120, Margin = new Thickness(0, 0, 0, 8) };
        duePanel.Children.Add(dueFilter);
        var due = new ListView(); Grid.SetRow(due, 1); duePanel.Children.Add(due);
        var dueColumns = new GridView(); due.View = dueColumns;
        dueColumns.Columns.Add(new GridViewColumn { Header = "单词", Width = 260, DisplayMemberBinding = new Binding(nameof(TodayStudyItem.Spelling)) });
        dueColumns.Columns.Add(new GridViewColumn { Header = "学习类型", Width = 110, DisplayMemberBinding = new Binding(nameof(TodayStudyItem.Kind)) });

        var addPanel = BuildRows(new Thickness(12), GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        tabs.Items.Add(new TabItem { Header = "加入新学", Content = addPanel });
        var sources = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) }; addPanel.Children.Add(sources);
        var pads = new ComboBox { DisplayMemberPath = nameof(CloudNotepad.Title), MinWidth = 155, MaxWidth = 230, Margin = new Thickness(0, 0, 7, 5) };
        sources.Children.Add(pads);
        var loadPad = StudyButton("读取词本", sources);
        var count = new TextBox { Width = 52, MaxLength = 4, ToolTip = "随机选词数量", Margin = new Thickness(3, 0, 7, 5) };
        sources.Children.Add(count);
        var random = StudyButton("随机选词", sources);

        var manualRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; Grid.SetRow(manualRow, 1); addPanel.Children.Add(manualRow);
        var stageWords = new Button { Content = "选入待确认", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(stageWords, Dock.Right); manualRow.Children.Add(stageWords);
        var input = new TextBox { AcceptsReturn = true, Height = 55, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "手动输入英文单词或短语，换行或用逗号分隔" };
        System.Windows.Automation.AutomationProperties.SetName(input, "手动输入英文单词或短语");
        manualRow.Children.Add(input);

        var choices = new ListBox { SelectionMode = SelectionMode.Multiple };
        var checkbox = new FrameworkElementFactory(typeof(CheckBox));
        checkbox.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Vocabulary.Spelling)));
        checkbox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ListBoxItem.IsSelected))
        { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1), Mode = BindingMode.TwoWay });
        checkbox.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 4, 0, 4));
        choices.ItemTemplate = new DataTemplate { VisualTree = checkbox };
        Grid.SetRow(choices, 2); addPanel.Children.Add(choices);
        var footer = new DockPanel { Margin = new Thickness(0, 9, 0, 0) }; Grid.SetRow(footer, 3); addPanel.Children.Add(footer);
        var confirm = new Button { Content = "确认添加", Style = (Style)FindResource("AccentButton") };
        DockPanel.SetDock(confirm, Dock.Right); footer.Children.Add(confirm);
        var selection = InputDialog.Text("已选 0 词", status: true); selection.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(selection);

        TodayStudyPlan? plan = null;
        var eligible = new Dictionary<string, Vocabulary>(StringComparer.Ordinal);
        var submitted = new HashSet<string>(StringComparer.Ordinal);
        var cloudIds = new HashSet<string>(StringComparer.Ordinal);
        string? loadedPad = null;
        var busy = false;
        var writing = false;
        Vocabulary[] Selected() => choices.SelectedItems.OfType<Vocabulary>().ToArray();
        void RenderSelection()
        {
            var length = choices.SelectedItems.Count;
            selection.Text = $"已选 {length} 词";
            confirm.IsEnabled = !busy && length is > 0 and <= MaiMemoClient.StudyBatchLimit;
            refresh.IsEnabled = choices.IsEnabled = input.IsEnabled = pads.IsEnabled = count.IsEnabled = !busy;
            loadPad.IsEnabled = random.IsEnabled = !busy && pads.SelectedItem is CloudNotepad;
            stageWords.IsEnabled = !busy && !string.IsNullOrWhiteSpace(input.Text);
        }
        void ReloadChoices(HashSet<string> selected)
        {
            var rows = eligible.Values.OrderBy(item => item.Spelling, StringComparer.OrdinalIgnoreCase).ToArray();
            choices.ItemsSource = rows;
            foreach (var item in rows.Where(item => selected.Contains(item.Id))) choices.SelectedItems.Add(item);
            RenderSelection();
        }
        void RenderDue() => due.ItemsSource = plan?.Items.Where(item => !item.IsFinished && (dueFilter.SelectedIndex == 0 || item.IsNew == (dueFilter.SelectedIndex == 1))).ToArray();
        async Task Refresh()
        {
            plan = await _api.GetTodayPlanAsync(cancellation);
            var remainingNew = plan.Items.Count(item => item.IsNew && !item.IsFinished);
            var remainingReview = plan.Items.Count(item => !item.IsNew && !item.IsFinished);
            var gap = plan.NewWordsNeeded is int needed ? $"还需加入 {needed} 词" : "新词缺口待同步";
            overview.Text = $"{plan.Day} · 已完成 {plan.Finished}/{plan.Target} · 待新学 {remainingNew} · 待复习 {remainingReview}\n{gap}"
                + (plan.Items.Count == MaiMemoClient.StudyBatchLimit ? " · 词表最多显示前 1000 条" : "");
            if (plan.NewWordsNeeded is int shortage) count.Text = Math.Min(shortage, MaiMemoClient.StudyBatchLimit).ToString();
            RenderDue();
        }
        async Task Run(string pending, Func<Task> action, bool write = false)
        {
            if (busy || cancellation.IsCancellationRequested) return;
            busy = true; writing = write; status.Text = pending; RenderSelection();
            try { await action(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    status.Text = UserError(error);
                    SetAccountError(error);
                }
            }
            finally { busy = writing = false; if (!cancellation.IsCancellationRequested) RenderSelection(); }
        }
        async Task ReadCloud()
        {
            var pad = (CloudNotepad)pads.SelectedItem;
            var result = await _api.GetTodayCandidatesAsync(pad.Id, null, cancellation);
            var selected = Selected().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in cloudIds) if (!selected.Contains(id)) eligible.Remove(id);
            cloudIds = result.Words.Select(item => item.Id).Where(id => !submitted.Contains(id)).ToHashSet(StringComparer.Ordinal);
            foreach (var item in result.Words.Where(item => cloudIds.Contains(item.Id))) eligible[item.Id] = item;
            loadedPad = pad.Id;
            ReloadChoices(selected);
            status.Text = $"词本中有 {cloudIds.Count} 个可选新词";
        }

        async Task ReadBooks()
        {
            var books = NotepadCombo.ItemsSource?.OfType<CloudNotepad>().ToArray() ?? [];
            if (books.Length == 0) books = (await _api.ListNotepadsAsync(cancellation)).ToArray();
            pads.ItemsSource = books;
            pads.SelectedItem = books.FirstOrDefault(book => book.Id == _preferences.NotepadId) ?? books.FirstOrDefault();
        }
        dialog.Loaded += async (_, _) => await Run("正在读取今日学习…", async () =>
        {
            try { await Refresh(); }
            finally { if (!cancellation.IsCancellationRequested) await ReadBooks(); }
            status.Text = plan!.NewWordsNeeded == null ? "请打开墨墨背单词并同步今日学习数据" : "";
        });
        refresh.Click += async (_, _) => await Run("正在刷新今日学习…", async () =>
        {
            await Refresh();
            if (pads.Items.Count == 0) await ReadBooks();
            status.Text = "今日学习已刷新";
        });
        dueFilter.SelectionChanged += (_, _) => RenderDue();
        choices.SelectionChanged += (_, _) => RenderSelection();
        input.TextChanged += (_, _) => RenderSelection();
        pads.SelectionChanged += (_, _) => RenderSelection();
        loadPad.Click += async (_, _) => await Run("正在核对词本中的新词…", ReadCloud);
        random.Click += async (_, _) => await Run("正在随机选词…", async () =>
        {
            if (!int.TryParse(count.Text, out var amount) || amount is < 1 or > MaiMemoClient.StudyBatchLimit)
                throw new ArgumentException("随机数量请输入 1 到 1000");
            if (((CloudNotepad)pads.SelectedItem).Id != loadedPad) await ReadCloud();
            var selectionIds = Selected().Select(item => item.Id).Where(id => !cloudIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
            var available = cloudIds.ToArray(); Random.Shared.Shuffle(available);
            selectionIds.UnionWith(available.Take(Math.Min(amount, MaiMemoClient.StudyBatchLimit - selectionIds.Count)));
            ReloadChoices(selectionIds);
            status.Text = $"已随机选中 {selectionIds.Count(cloudIds.Contains)} 词，确认后才会添加";
        });
        stageWords.Click += async (_, _) => await Run("正在核对输入的单词…", async () =>
        {
            var result = await _api.GetTodayCandidatesAsync(null, input.Text, cancellation);
            var selected = Selected().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var item in result.Words.Where(item => !submitted.Contains(item.Id)))
            {
                eligible[item.Id] = item;
                if (selected.Count < MaiMemoClient.StudyBatchLimit) selected.Add(item.Id);
            }
            ReloadChoices(selected); input.Clear();
            status.Text = $"找到 {result.Words.Count} 个可选新词 · {result.Excluded} 个已在计划或未被墨墨收录";
        });
        confirm.Click += async (_, _) =>
        {
            var ids = Selected().Select(item => item.Id).ToArray();
            var day = plan?.Day ?? MaiMemoClient.TodayPlanDay();
            if (ids.Length == 0 || !TodayStudyConfirmationDialog.ConfirmBatch(dialog, ids.Length)) return;
            await Run("正在确认并添加新词…", async () =>
            {
                var result = await _api.AddTodayBatchAsync(ids, day, cancellation);
                submitted.UnionWith(ids); cloudIds.ExceptWith(ids);
                foreach (var id in ids) eligible.Remove(id);
                ReloadChoices([]); status.Text = result.Summary;
                try { await Refresh(); }
                catch (MaiMemoException) { status.Text = result.Summary + " · 今日学习暂未刷新"; }
                if (_word.Length > 0) { _todayStudyStates.Remove(_word); _ = RetryTodayStudyStatusAsync(_word); }
            }, write: true);
        };
        dialog.Closing += (_, args) => { if (writing && !cancellation.IsCancellationRequested) args.Cancel = true; };
        dialog.Closed += (_, _) => session.Cancel();
        RenderSelection(); dialog.ShowDialog();
    }

    private static Grid BuildRows(Thickness margin, params GridLength[] heights)
    {
        var grid = new Grid { Margin = margin };
        foreach (var height in heights) grid.RowDefinitions.Add(new RowDefinition { Height = height });
        return grid;
    }

    private static Button StudyButton(string title, Panel parent)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 7, 5) };
        parent.Children.Add(button);
        return button;
    }
}
