using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal sealed class QuickLookupBar : Window
{
    private readonly LocalDictionary _dictionary;
    private readonly MnemonicStore _mnemonics;
    private readonly Func<string, EnglishAccent, Task> _speak;
    private readonly Func<string, Task<string?>> _bookmark;
    private readonly Action<string> _openMain;
    private readonly Func<string?> _accountKey;
    private readonly TextBox _input;
    private readonly StackPanel _resultPanel;
    private readonly Border _resultContainer;
    private readonly TextBlock _wordText;
    private readonly TextBlock _phoneticText;
    private readonly TextBlock _meaningText;
    private readonly Border _mnemonicBox;
    private readonly TextBlock _mnemonicText;
    private readonly WrapPanel _suggestionsPanel;
    private readonly TextBlock _statusText;
    private readonly Button _speakBtn;
    private readonly Button _bookmarkBtn;
    private readonly Button _openBtn;
    private string _currentWord = "";
    private bool _suppressDeactivate;

    internal QuickLookupBar(
        LocalDictionary dictionary,
        MnemonicStore mnemonics,
        Func<string, EnglishAccent, Task> speak,
        Func<string, Task<string?>> bookmark,
        Action<string> openMain,
        Func<string?> accountKey)
    {
        _dictionary = dictionary;
        _mnemonics = mnemonics;
        _speak = speak;
        _bookmark = bookmark;
        _openMain = openMain;
        _accountKey = accountKey;

        Title = "快速查词 · 墨墨搜词";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;

        var layout = new StackPanel();

        // Top search bar
        var searchRow = new Grid { Margin = new Thickness(16, 14, 14, 14) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchIcon = new TextBlock
        {
            Text = "\uE721",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 12, 0)
        };
        searchIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        searchRow.Children.Add(searchIcon);

        _input = new TextBox
        {
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_input, 1);
        searchRow.Children.Add(_input);

        var escBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Esc", FontSize = 10.5, FontWeight = FontWeights.SemiBold }
        };
        escBadge.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        ((TextBlock)escBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(escBadge, 2);
        searchRow.Children.Add(escBadge);

        layout.Children.Add(searchRow);

        // Result container
        _resultPanel = new StackPanel();

        var headRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var headActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(headActions, Dock.Right);

        _speakBtn = new Button
        {
            Content = "\uE767 朗读",
            FontFamily = new FontFamily("Segoe MDL2 Assets, Microsoft YaHei UI"),
            FontSize = 11.5,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "播放美音"
        };
        if (Application.Current.TryFindResource("PillButton") is Style pillStyle) _speakBtn.Style = pillStyle;

        _bookmarkBtn = new Button
        {
            Content = "＋ 收藏",
            FontSize = 11.5,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };

        _openBtn = new Button
        {
            Content = "展开主窗口 →",
            FontSize = 11.5,
            Padding = new Thickness(11, 4, 11, 4)
        };
        if (Application.Current.TryFindResource("AccentButton") is Style accentStyle) _openBtn.Style = accentStyle;

        headActions.Children.Add(_speakBtn);
        headActions.Children.Add(_bookmarkBtn);
        headActions.Children.Add(_openBtn);
        headRow.Children.Add(headActions);

        var wordWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        _wordText = new TextBlock { FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Bottom };
        _wordText.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        _phoneticText = new TextBlock { FontSize = 13.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) };
        _phoneticText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        wordWrap.Children.Add(_wordText);
        wordWrap.Children.Add(_phoneticText);
        headRow.Children.Add(wordWrap);
        _resultPanel.Children.Add(headRow);

        _meaningText = new TextBlock { FontSize = 14.5, LineHeight = 23, TextWrapping = TextWrapping.Wrap };
        _meaningText.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _resultPanel.Children.Add(_meaningText);

        _mnemonicText = new TextBlock { FontSize = 12.5, LineHeight = 19, TextWrapping = TextWrapping.Wrap };
        _mnemonicText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        _mnemonicBox = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 8, 11, 8),
            Margin = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Child = _mnemonicText
        };
        _mnemonicBox.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        _mnemonicBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _resultPanel.Children.Add(_mnemonicBox);

        _suggestionsPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        _resultPanel.Children.Add(_suggestionsPanel);

        _statusText = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _resultPanel.Children.Add(_statusText);

        _resultContainer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 14, 18, 16),
            Visibility = Visibility.Collapsed,
            Child = _resultPanel
        };
        _resultContainer.SetResourceReference(Border.BorderBrushProperty, "RuleBrush");
        layout.Children.Add(_resultContainer);

        var shell = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1.2),
            Margin = new Thickness(16, 12, 16, 20),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.22, Color = Color.FromRgb(8, 28, 46) },
            Child = layout
        };
        shell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        shell.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = shell;

        _input.TextChanged += (_, _) => UpdateLiveResult();
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Hide(); }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var target = _currentWord.Length > 0 ? _currentWord : _input.Text.Trim();
                if (target.Length > 0) OpenInMain(target);
            }
        };
        _speakBtn.Click += async (_, _) =>
        {
            if (_currentWord.Length == 0) return;
            _suppressDeactivate = true;
            try { await _speak(_currentWord, EnglishAccent.American); }
            finally { _suppressDeactivate = false; }
        };
        _bookmarkBtn.Click += async (_, _) =>
        {
            if (_currentWord.Length == 0) return;
            _bookmarkBtn.IsEnabled = false;
            _statusText.Visibility = Visibility.Visible;
            _statusText.Text = "正在加入云词本…";
            _suppressDeactivate = true;
            try
            {
                var message = await _bookmark(_currentWord);
                _statusText.Text = message ?? $"已收藏「{_currentWord}」";
            }
            finally
            {
                _suppressDeactivate = false;
                _bookmarkBtn.IsEnabled = true;
            }
        };
        _openBtn.Click += (_, _) =>
        {
            var target = _currentWord.Length > 0 ? _currentWord : _input.Text.Trim();
            if (target.Length > 0) OpenInMain(target);
        };
        Deactivated += (_, _) => { if (!_suppressDeactivate && IsVisible) Hide(); };
    }

    internal void Popup()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + Math.Min(140, work.Height * 0.18);
        _statusText.Visibility = Visibility.Collapsed;
        _statusText.Text = "";
        Show();
        Activate();
        _input.Focus();
        _input.SelectAll();
        UpdateLiveResult();
    }

    private void OpenInMain(string word)
    {
        Hide();
        _openMain(word);
    }

    private void UpdateLiveResult()
    {
        var raw = _input.Text.Trim();
        _statusText.Visibility = Visibility.Collapsed;
        _suggestionsPanel.Children.Clear();
        _suggestionsPanel.Visibility = Visibility.Collapsed;

        if (raw.Length == 0)
        {
            _currentWord = "";
            _resultContainer.Visibility = Visibility.Collapsed;
            return;
        }

        _resultContainer.Visibility = Visibility.Visible;
        var resolved = ExampleText.ResolveLookupWord(raw, candidate => _dictionary.Lookup(candidate) != null);
        var entry = _dictionary.Lookup(resolved);
        var suggestions = _dictionary.Suggest(raw, 6);

        if (entry == null && suggestions.Count > 0 && raw.Length >= 2)
        {
            entry = _dictionary.Lookup(suggestions[0]);
        }

        if (entry != null)
        {
            _currentWord = entry.word;
            _wordText.Text = entry.word;
            _phoneticText.Text = string.IsNullOrWhiteSpace(entry.phonetic) ? "" : $"/{entry.phonetic.Trim('/')}/";
            _meaningText.Text = entry.DisplayMeaning;
            _speakBtn.Visibility = _bookmarkBtn.Visibility = Visibility.Visible;

            var note = _mnemonics.Get(entry.word, _accountKey()).FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(note))
            {
                _mnemonicText.Text = "助记：" + (note.Length <= 150 ? note : note[..150] + "…");
                _mnemonicBox.Visibility = Visibility.Visible;
            }
            else _mnemonicBox.Visibility = Visibility.Collapsed;

            if (suggestions.Count > 1)
            {
                AddSuggestionPills(suggestions.Where(s => !s.Equals(entry.word, StringComparison.OrdinalIgnoreCase)).Take(5));
            }
        }
        else
        {
            _currentWord = raw;
            _wordText.Text = raw;
            _phoneticText.Text = "";
            _mnemonicBox.Visibility = Visibility.Collapsed;
            _speakBtn.Visibility = _bookmarkBtn.Visibility = Visibility.Collapsed;

            var fuzzy = _dictionary.SuggestSpellingCorrections(raw, 5);
            if (fuzzy.Count > 0)
            {
                _meaningText.Text = "离线词库未找到该词，你是不是要查：";
                AddSuggestionPills(fuzzy);
            }
            else
            {
                _meaningText.Text = "按 Enter 在主窗口中联网查询或用中文反查。";
            }
        }
    }

    private void AddSuggestionPills(IEnumerable<string> words)
    {
        var pillStyle = Application.Current.TryFindResource("PillButton") as Style;
        var count = 0;
        foreach (var candidate in words)
        {
            var word = candidate;
            var btn = new Button
            {
                Content = word,
                FontSize = 11.5,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 4)
            };
            if (pillStyle != null) btn.Style = pillStyle;
            btn.Click += (_, _) => { _input.Text = word; _input.CaretIndex = word.Length; };
            _suggestionsPanel.Children.Add(btn);
            count++;
        }
        _suggestionsPanel.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
