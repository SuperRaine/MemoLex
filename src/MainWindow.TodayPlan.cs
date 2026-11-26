using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
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
        dialog.Height = Math.Min(760, SystemParameters.WorkArea.Height - 24);
        dialog.MinWidth = 570; dialog.MinHeight = 495;
        WindowChrome.GetWindowChrome(dialog).ResizeBorderThickness = new Thickness(6);
        using var accountChanged = cancellation.Register(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (dialog.IsVisible) dialog.Close();
        })));
        var star = new GridLength(1, GridUnitType.Star);
        var body = BuildRows(new Thickness(16, 10, 16, 10), GridLength.Auto, GridLength.Auto, star, GridLength.Auto);
        InputDialog.SetBody(dialog, body);

        var headerCard = new Border
        {
            Background = Brush("#F4FAFF"), BorderBrush = Brush("#DAEAF7"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 10, 14, 11), Margin = new Thickness(0, 0, 0, 10)
        };
        var headerStack = new StackPanel();
        headerCard.Child = headerStack;
        var header = new DockPanel();
        var refresh = new Button { Content = "刷新", Padding = new Thickness(11, 5, 11, 5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(refresh, Dock.Right); header.Children.Add(refresh);
        var overview = InputDialog.Text("正在读取今日学习…", status: true);
        overview.FontSize = 13; overview.FontWeight = FontWeights.SemiBold; overview.Foreground = Brush("#163952");
        overview.LineHeight = 19; overview.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(overview); headerStack.Children.Add(header);
        var studyTrack = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Brush("#DFEEF9"), Margin = new Thickness(0, 8, 0, 0), ClipToBounds = true };
        var studyFill = new Border
        {
            Height = 6, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3),
            Background = new LinearGradientBrush(Color.FromRgb(0x34, 0xBC, 0xFC), Color.FromRgb(0x0D, 0x85, 0xC1), new Point(0, 0), new Point(1, 0))
        };
        studyTrack.Child = studyFill;
        headerStack.Children.Add(studyTrack);
        body.Children.Add(headerCard);
        var page = new Grid();
        var card = new Border
        {
            Background = Brush("#F7FBFE"), BorderBrush = Brush("#DFEAF3"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9), Child = page
        };
        Grid.SetRow(card, 2); body.Children.Add(card);
        var status = InputDialog.Text("", status: true); status.Margin = new Thickness(0, 5, 0, 0); status.LineHeight = 18;
        Grid.SetRow(status, 3); body.Children.Add(status);

        var duePanel = BuildRows(new Thickness(0), GridLength.Auto, star);
        var addPanel = BuildRows(new Thickness(0), GridLength.Auto, GridLength.Auto, star, GridLength.Auto);
        page.Children.Add(duePanel); page.Children.Add(addPanel);
        var dueFilter = 0;
        var tab = 0;
        var queries = new[] { "", "" };
        var due = StudyList(includeKind: true);
        Grid.SetRow(due, 1); duePanel.Children.Add(due);
        var dueEmpty = InputDialog.Text("", status: true);
        dueEmpty.Margin = new Thickness(10, 36, 10, 0);
        dueEmpty.VerticalAlignment = VerticalAlignment.Top;
        dueEmpty.IsHitTestVisible = false;
        Grid.SetRow(dueEmpty, 1); duePanel.Children.Add(dueEmpty);

        var searchBar = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var conceal = new CheckBox
        {
            Content = "遮住释义",
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
        };
        DockPanel.SetDock(conceal, Dock.Right); searchBar.Children.Add(conceal);
        var searchLabel = InputDialog.Text("搜索", status: true);
        searchLabel.VerticalAlignment = VerticalAlignment.Center; searchLabel.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(searchLabel, Dock.Left); searchBar.Children.Add(searchLabel);
        var search = new TextBox { ToolTip = "搜索单词或释义", MinHeight = 30, Padding = new Thickness(9, 4, 9, 4) };
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索单词或释义");
        searchBar.Children.Add(search);

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 7) }; addPanel.Children.Add(toolbar);
        var manualButton = new Button { Content = "手动输入", Padding = new Thickness(10, 5, 10, 5), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(manualButton, Dock.Right); toolbar.Children.Add(manualButton);
        var sources = new WrapPanel(); toolbar.Children.Add(sources);
        var pads = new ComboBox { DisplayMemberPath = nameof(CloudNotepad.Title), MinWidth = 136, MaxWidth = 180, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
        sources.Children.Add(pads);
        var loadPad = StudyButton("读取词本", sources);
        var count = new TextBox { Width = 47, Height = 30, MaxLength = 4, ToolTip = "随机选词数量", Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 6, 0) };
        sources.Children.Add(count);
        var random = StudyButton("随机选词", sources);

        var manualRow = new DockPanel { Margin = new Thickness(0, 0, 0, 7), Visibility = Visibility.Collapsed };
        Grid.SetRow(manualRow, 1); addPanel.Children.Add(manualRow);
        var stageWords = new Button { Content = "选入待确认", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(stageWords, Dock.Right); manualRow.Children.Add(stageWords);
        var input = new TextBox { AcceptsReturn = true, Height = 46, Padding = new Thickness(9, 4, 9, 4), TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "手动输入英文单词或短语，换行或用逗号分隔" };
        System.Windows.Automation.AutomationProperties.SetName(input, "手动输入英文单词或短语");
        manualRow.Children.Add(input);
        void ShowManualInput(bool visible)
        {
            manualRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            manualButton.Content = visible ? "收起输入" : "手动输入";
            if (visible) input.Focus();
            else manualButton.Focus();
        }
        manualButton.Click += (_, _) => ShowManualInput(manualRow.Visibility != Visibility.Visible);

        var choices = StudyList(includeKind: false);
        choices.SelectionMode = SelectionMode.Extended;
        Grid.SetRow(choices, 2); addPanel.Children.Add(choices);
        var choicesEmpty = InputDialog.Text("", status: true);
        choicesEmpty.Margin = new Thickness(10, 36, 10, 0);
        choicesEmpty.VerticalAlignment = VerticalAlignment.Top;
        choicesEmpty.IsHitTestVisible = false;
        Grid.SetRow(choicesEmpty, 2); addPanel.Children.Add(choicesEmpty);
        var footer = new DockPanel { Margin = new Thickness(0, 7, 0, 0) }; Grid.SetRow(footer, 3); addPanel.Children.Add(footer);
        var confirm = new Button { Content = "确认添加", Style = (Style)FindResource("AccentButton"), Padding = new Thickness(10, 5, 10, 5) };
        DockPanel.SetDock(confirm, Dock.Right); footer.Children.Add(confirm);
        var selectionActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(selectionActions, Dock.Left); footer.Children.Add(selectionActions);
        var selectAll = StudyButton("全选当前结果", selectionActions);
        var clear = StudyButton("清除选择", selectionActions);
        var selection = InputDialog.Text("已选 0 词", status: true);
        selection.VerticalAlignment = VerticalAlignment.Center;
        selection.TextWrapping = TextWrapping.NoWrap;
        selection.TextTrimming = TextTrimming.CharacterEllipsis;
        selection.ToolTip = "按住 Ctrl 多选，按住 Shift 连选";
        footer.Children.Add(selection);

        TodayStudyPlan? plan = null;
        var eligible = new Dictionary<string, Vocabulary>(StringComparer.Ordinal);
        var submitted = new HashSet<string>(StringComparer.Ordinal);
        var cloudIds = new HashSet<string>(StringComparer.Ordinal);
        var meanings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        WordListRow[] dueRows = [], candidateRows = [];
        string? loadedPad = null;
        var busy = false;
        var writing = false;
        string[] Selected() => choices.SelectedItems.OfType<WordListRow>().Select(item => item.Id).ToArray();
        bool updatingSelection = false;
        void RenderSelection()
        {
            var length = choices.SelectedItems.Count;
            selection.Text = $"已选 {length} 词 · Ctrl / Shift 多选";
            confirm.IsEnabled = !busy && length is > 0 and <= MaiMemoClient.StudyBatchLimit;
            selectAll.IsEnabled = !busy && choices.Items.Count > 0;
            clear.IsEnabled = !busy && length > 0;
            refresh.IsEnabled = choices.IsEnabled = manualButton.IsEnabled = input.IsEnabled = pads.IsEnabled = count.IsEnabled = !busy;
            loadPad.IsEnabled = random.IsEnabled = !busy && pads.SelectedItem is CloudNotepad;
            stageWords.IsEnabled = !busy && !string.IsNullOrWhiteSpace(input.Text);
        }
        static bool Match(WordListRow row, string query) => row.Word.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Meaning.Contains(query, StringComparison.OrdinalIgnoreCase);
        void RenderChoices(HashSet<string> selected)
        {
            var query = (tab == 1 ? search.Text : queries[1]).Trim();
            var rows = candidateRows.Where(row => Match(row, query)).ToArray();
            updatingSelection = true;
            choices.ItemsSource = rows;
            foreach (var row in rows.Where(row => selected.Contains(row.Id))) choices.SelectedItems.Add(row);
            updatingSelection = false;
            choicesEmpty.Text = rows.Length != 0 ? "" : candidateRows.Length == 0
                ? "选择云词本或手动输入单词" : "未找到匹配的单词";
            RenderSelection();
        }
        void RenderDue()
        {
            var query = (tab == 0 ? search.Text : queries[0]).Trim();
            var rows = dueRows.Where(row => (dueFilter == 0 || row.Kind == (dueFilter == 1 ? "新学" : "复习")) && Match(row, query)).ToArray();
            due.ItemsSource = rows;
            dueEmpty.Text = rows.Length != 0 ? "" : plan == null ? "" : "没有符合条件的待学词汇";
        }
        void RenderSearch()
        {
            if (tab == 0) RenderDue();
            else RenderChoices(Selected().ToHashSet(StringComparer.Ordinal));
        }
        async Task LoadMeanings(IEnumerable<string> words)
        {
            var missing = words.Distinct(StringComparer.OrdinalIgnoreCase).Where(word => !meanings.ContainsKey(word)).ToArray();
            var found = await Task.Run(() => missing.ToDictionary(word => word, word =>
            {
                cancellation.ThrowIfCancellationRequested();
                return _dictionary.Lookup(word)?.DisplayMeaning ?? "";
            }, StringComparer.OrdinalIgnoreCase), cancellation);
            foreach (var pair in found) meanings[pair.Key] = pair.Value;
        }
        WordListRow Row(string id, string word, string kind = "") =>
            new(word, meanings.GetValueOrDefault(word, ""), _onlineDictionary, conceal: conceal.IsChecked == true) { Id = id, Kind = kind };
        async Task ReloadChoices(HashSet<string> selected)
        {
            var entries = eligible.Values.OrderBy(item => item.Spelling, StringComparer.OrdinalIgnoreCase).ToArray();
            await LoadMeanings(entries.Select(item => item.Spelling));
            var existing = candidateRows.ToDictionary(row => row.Id, StringComparer.Ordinal);
            candidateRows = entries.Select(item => existing.GetValueOrDefault(item.Id) ?? Row(item.Id, item.Spelling)).ToArray();
            RenderChoices(selected);
        }
        void ClearCandidateSearch()
        {
            queries[1] = "";
            if (tab == 1) search.Clear();
        }
        var tabs = StudySegments(["待学词汇", "加入新学"], index =>
        {
            queries[tab] = search.Text;
            tab = index;
            duePanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            addPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (search.Text != queries[index]) search.Text = queries[index];
            else RenderSearch();
        });
        var navigation = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        tabs.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(tabs, Dock.Left); navigation.Children.Add(tabs);
        navigation.Children.Add(searchBar);
        Grid.SetRow(navigation, 1); body.Children.Add(navigation);
        duePanel.Children.Add(StudySegments(["全部待学", "待新学", "待复习"], index =>
        {
            dueFilter = index;
            RenderDue();
        }));
        void UpdateStudyBar()
        {
            if (studyTrack.ActualWidth <= 0 || plan == null) return;
            var ratio = plan.Target > 0 ? Math.Clamp((double)plan.Finished / plan.Target, 0, 1) : 0;
            Desktop.UiMotion.AnimateWidth(studyFill, studyTrack.ActualWidth * ratio, 320);
        }
        studyTrack.SizeChanged += (_, _) => UpdateStudyBar();
        async Task Refresh()
        {
            plan = await _api.GetTodayPlanAsync(cancellation);
            var remainingNew = plan.Items.Count(item => item.IsNew && !item.IsFinished);
            var remainingReview = plan.Items.Count(item => !item.IsNew && !item.IsFinished);
            var gap = plan.NewWordsNeeded is int needed ? $"还需加入 {needed} 词" : "新词缺口待同步";
            overview.Text = $"{plan.Day} · 已完成 {plan.Finished}/{plan.Target} · 待新学 {remainingNew} · 待复习 {remainingReview} · {gap}"
                + (plan.Items.Count == MaiMemoClient.StudyBatchLimit ? " · 词表最多显示前 1000 条" : "");
            UpdateStudyBar();
            if (plan.NewWordsNeeded is int shortage) count.Text = Math.Min(shortage, MaiMemoClient.StudyBatchLimit).ToString();
            var entries = plan.Items.Where(item => !item.IsFinished).ToArray();
            await LoadMeanings(entries.Select(item => item.Spelling));
            dueRows = entries.Select(item => Row(item.Id, item.Spelling, item.Kind)).ToArray();
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
            var selected = Selected().ToHashSet(StringComparer.Ordinal);
            foreach (var id in cloudIds) if (!selected.Contains(id)) eligible.Remove(id);
            cloudIds = result.Words.Select(item => item.Id).Where(id => !submitted.Contains(id)).ToHashSet(StringComparer.Ordinal);
            foreach (var item in result.Words.Where(item => cloudIds.Contains(item.Id))) eligible[item.Id] = item;
            loadedPad = pad.Id;
            await ReloadChoices(selected);
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
        search.TextChanged += (_, _) => RenderSearch();
        conceal.Checked += (_, _) => { foreach (var row in dueRows.Concat(candidateRows)) row.SetConceal(true); };
        conceal.Unchecked += (_, _) => { foreach (var row in dueRows.Concat(candidateRows)) row.SetConceal(false); };
        choices.SelectionChanged += (_, _) => { if (!updatingSelection) RenderSelection(); };
        selectAll.Click += (_, _) => choices.SelectAll();
        clear.Click += (_, _) => choices.UnselectAll();
        choices.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                choices.SelectAll();
                args.Handled = true;
            }
        };
        input.TextChanged += (_, _) => RenderSelection();
        pads.SelectionChanged += (_, _) => RenderSelection();
        loadPad.Click += async (_, _) => await Run("正在核对词本中的新词…", ReadCloud);
        random.Click += async (_, _) => await Run("正在随机选词…", async () =>
        {
            if (!int.TryParse(count.Text, out var amount) || amount is < 1 or > MaiMemoClient.StudyBatchLimit)
                throw new ArgumentException("随机数量请输入 1 到 1000");
            if (((CloudNotepad)pads.SelectedItem).Id != loadedPad) await ReadCloud();
            var available = cloudIds.ToArray(); Random.Shared.Shuffle(available);
            var picks = available.Take(amount).ToHashSet(StringComparer.Ordinal);
            choices.UnselectAll();
            ClearCandidateSearch();
            await ReloadChoices(picks);
            status.Text = $"已随机选中 {picks.Count} 词，确认后才会添加";
        });
        stageWords.Click += async (_, _) => await Run("正在核对输入的单词…", async () =>
        {
            var result = await _api.GetTodayCandidatesAsync(null, input.Text, cancellation);
            var selected = Selected().ToHashSet(StringComparer.Ordinal);
            foreach (var item in result.Words.Where(item => !submitted.Contains(item.Id)))
            {
                eligible[item.Id] = item;
                if (selected.Count < MaiMemoClient.StudyBatchLimit) selected.Add(item.Id);
            }
            ClearCandidateSearch();
            await ReloadChoices(selected); input.Clear();
            manualRow.Visibility = Visibility.Collapsed;
            manualButton.Content = "手动输入";
            _ = Dispatcher.BeginInvoke(new Action(() => choices.Focus()), System.Windows.Threading.DispatcherPriority.Input);
            status.Text = $"找到 {result.Words.Count} 个可选新词 · {result.Excluded} 个已在计划或未被墨墨收录";
        });
        confirm.Click += async (_, _) =>
        {
            var ids = Selected();
            var day = plan?.Day ?? MaiMemoClient.TodayPlanDay();
            if (ids.Length == 0 || !TodayStudyConfirmationDialog.ConfirmBatch(dialog, ids.Length)) return;
            await Run("正在确认并添加新词…", async () =>
            {
                var result = await _api.AddTodayBatchAsync(ids, day, cancellation);
                submitted.UnionWith(ids); cloudIds.ExceptWith(ids);
                foreach (var id in ids) eligible.Remove(id);
                await ReloadChoices([]); status.Text = result.Summary;
                try { await Refresh(); }
                catch (MaiMemoException) { status.Text = result.Summary + " · 今日学习暂未刷新"; }
                if (_word.Length > 0) { _todayStudyStates.Remove(_word); _ = RetryTodayStudyStatusAsync(_word); }
            }, write: true);
        };
        dialog.Closing += (_, args) => { if (writing && !cancellation.IsCancellationRequested) args.Cancel = true; };
        dialog.Closed += (_, _) => session.Cancel();
        RenderChoices([]); dialog.ShowDialog();
    }

    private static Grid BuildRows(Thickness margin, params GridLength[] heights)
    {
        var grid = new Grid { Margin = margin };
        foreach (var height in heights) grid.RowDefinitions.Add(new RowDefinition { Height = height });
        return grid;
    }

    private static Button StudyButton(string title, Panel parent)
    {
        var button = new Button { Content = title, MinHeight = 30, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 0) };
        parent.Children.Add(button);
        return button;
    }

    private static Border StudySegments(string[] titles, Action<int> selected)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var border = new Border
        {
            Child = buttons, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 9),
            Padding = new Thickness(3), CornerRadius = new CornerRadius(9), Background = Brush("#EBF3F9")
        };
        void Render(int current)
        {
            for (var index = 0; index < buttons.Children.Count; index++)
            {
                var button = (Button)buttons.Children[index];
                button.Background = index == current ? Brushes.White : Brushes.Transparent;
                button.Foreground = index == current ? Brush("#0A7FB3") : Brush("#58768E");
                button.FontWeight = index == current ? FontWeights.SemiBold : FontWeights.Normal;
                button.Effect = index == current ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.09, Color = Color.FromRgb(0x0E, 0x4B, 0x73) } : null;
            }
        }
        for (var index = 0; index < titles.Length; index++)
        {
            var segment = index;
            var button = new Button { Content = titles[index], Padding = new Thickness(14, 5, 14, 5), BorderThickness = new Thickness(0) };
            button.Click += (_, _) => { Render(segment); selected(segment); };
            buttons.Children.Add(button);
        }
        Render(0);
        selected(0);
        return border;
    }

    private static ListView StudyList(bool includeKind)
    {
        var list = new ListView { BorderThickness = new Thickness(0), Background = Brushes.White };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        var view = new GridView();
        list.View = view;
        view.Columns.Add(new GridViewColumn { Header = "单词", Width = 152, DisplayMemberBinding = new Binding(nameof(WordListRow.Word)) });
        var meaning = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(WordListMeaningText));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(WordListRow.DisplayMeaning)));
        text.SetBinding(ToolTipProperty, new Binding(nameof(WordListRow.MeaningTooltip)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(MarginProperty, includeKind ? new Thickness(0, 6, 0, 6) : new Thickness(0, 4, 0, 4));
        text.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((sender, args) =>
        {
            if (sender is TextBlock { DataContext: WordListRow { CanToggleMeaning: true } row })
            {
                row.ToggleMeaning();
                args.Handled = true;
            }
        }));
        meaning.VisualTree = text;
        var meaningColumn = new GridViewColumn { Header = "释义", CellTemplate = meaning, Width = 280 };
        view.Columns.Add(meaningColumn);
        if (includeKind)
            view.Columns.Add(new GridViewColumn { Header = "学习类型", Width = 83, DisplayMemberBinding = new Binding(nameof(WordListRow.Kind)) });
        list.SizeChanged += (_, _) => meaningColumn.Width = Math.Max(160, list.ActualWidth - (includeKind ? 255 : 172));
        return list;
    }
}
