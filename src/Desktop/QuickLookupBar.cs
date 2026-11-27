using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal sealed class QuickLookupBar : Window
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly LocalDictionary _dictionary;
    private readonly MnemonicStore _mnemonics;
    private readonly Func<string, EnglishAccent, Task> _speak;
    private readonly Func<string, Task<string?>> _bookmark;
    private readonly Action<string> _openMain;
    private readonly Func<string?> _accountKey;
    private readonly Border _shell;
    private readonly TextBox _input;
    private readonly TextBlock _placeholder;
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
    private readonly LowLevelMouseProc _mouseProc;
    private IntPtr _mouseHook;
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
        Width = 636;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;

        var layout = new StackPanel();

        // Borderless Spotlight search header (no ugly nested TextBox frame!)
        var searchRow = new Grid { Margin = new Thickness(20, 15, 16, 15) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchIcon = new TextBlock
        {
            Text = "\uE721",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 13, 0)
        };
        searchIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        searchRow.Children.Add(searchIcon);

        var inputHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        _placeholder = new TextBlock
        {
            Text = "输入英文或中文查词…",
            FontSize = 17.5,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(2, 0, 0, 0)
        };
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");

        // Clean borderless ControlTemplate so WPF never draws a boxed TextBox border or double focus halo
        var borderlessTemplate = new ControlTemplate(typeof(TextBox));
        var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        scrollViewer.SetValue(FocusableProperty, false);
        scrollViewer.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderlessTemplate.VisualTree = scrollViewer;

        _input = new TextBox
        {
            Template = borderlessTemplate,
            FontSize = 18.5,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4, 8, 4),
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _input.SetResourceReference(Control.ForegroundProperty, "InkStrongBrush");
        _input.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "AccentDeepBrush");

        inputHost.Children.Add(_placeholder);
        inputHost.Children.Add(_input);
        Grid.SetColumn(inputHost, 1);
        searchRow.Children.Add(inputHost);

        var escBadge = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3.5, 8, 3.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Child = new TextBlock { Text = "Esc", FontSize = 11, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold }
        };
        escBadge.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        escBadge.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        ((TextBlock)escBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(escBadge, 2);
        searchRow.Children.Add(escBadge);

        layout.Children.Add(searchRow);

        // Result container
        _resultPanel = new StackPanel();

        var headRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var headActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(headActions, Dock.Right);

        // Separate icon & text blocks so Chinese never falls back to Segoe MDL2 Assets (fixes □□ bug!)
        var speakContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        speakContent.Children.Add(new TextBlock
        {
            Text = "\uE767",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        speakContent.Children.Add(new TextBlock
        {
            Text = "朗读",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        _speakBtn = new Button
        {
            Content = speakContent,
            Padding = new Thickness(11, 5, 11, 5),
            Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "播放美音"
        };
        if (Application.Current.TryFindResource("PillButton") is Style pillStyle) _speakBtn.Style = pillStyle;

        _bookmarkBtn = new Button
        {
            Content = "＋ 收藏",
            FontSize = 12,
            Padding = new Thickness(11, 5, 11, 5),
            Margin = new Thickness(0, 0, 7, 0)
        };

        _openBtn = new Button
        {
            Content = "展开主窗口 →",
            FontSize = 12,
            Padding = new Thickness(13, 5, 13, 5)
        };
        if (Application.Current.TryFindResource("AccentButton") is Style accentStyle) _openBtn.Style = accentStyle;

        headActions.Children.Add(_speakBtn);
        headActions.Children.Add(_bookmarkBtn);
        headActions.Children.Add(_openBtn);
        headRow.Children.Add(headActions);

        var wordWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        _wordText = new TextBlock
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        _wordText.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        _phoneticText = new TextBlock
        {
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 3)
        };
        _phoneticText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        wordWrap.Children.Add(_wordText);
        wordWrap.Children.Add(_phoneticText);
        headRow.Children.Add(wordWrap);
        _resultPanel.Children.Add(headRow);

        _meaningText = new TextBlock { FontSize = 15, LineHeight = 25, TextWrapping = TextWrapping.Wrap };
        _meaningText.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _resultPanel.Children.Add(_meaningText);

        var noteRow = new Grid();
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var noteBadge = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0),
            Child = new TextBlock { Text = "记", FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        noteBadge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
        ((TextBlock)noteBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        noteRow.Children.Add(noteBadge);

        _mnemonicText = new TextBlock { FontSize = 12.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap };
        _mnemonicText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        Grid.SetColumn(_mnemonicText, 1);
        noteRow.Children.Add(_mnemonicText);

        _mnemonicBox = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 12, 0, 0),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Child = noteRow
        };
        _mnemonicBox.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        _mnemonicBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _resultPanel.Children.Add(_mnemonicBox);

        _suggestionsPanel = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        _resultPanel.Children.Add(_suggestionsPanel);

        _statusText = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _resultPanel.Children.Add(_statusText);

        var footerBar = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(0, 9, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = "↩ 回车在主窗口查看完整释义 · 点击候选词切换 · Esc 关闭",
                FontSize = 11
            }
        };
        footerBar.SetResourceReference(Border.BorderBrushProperty, "RuleBrush");
        ((TextBlock)footerBar.Child).SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        _resultPanel.Children.Add(footerBar);

        _resultContainer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(22, 16, 22, 14),
            Visibility = Visibility.Collapsed,
            Child = _resultPanel
        };
        _resultContainer.SetResourceReference(Border.BorderBrushProperty, "RuleBrush");
        layout.Children.Add(_resultContainer);

        _shell = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1.2),
            Margin = new Thickness(18, 12, 18, 22),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 10, Opacity = 0.24, Color = Color.FromRgb(8, 28, 46) },
            Child = layout
        };
        _shell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        _shell.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = _shell;

        _mouseProc = MouseHookCallback;

        _input.TextChanged += (_, _) =>
        {
            _placeholder.Visibility = string.IsNullOrEmpty(_input.Text) ? Visibility.Visible : Visibility.Collapsed;
            UpdateLiveResult();
        };
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; HideBar(); }
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
        Deactivated += (_, _) => { if (!_suppressDeactivate && IsVisible) HideBar(); };
        Closed += (_, _) => UninstallMouseHook();
    }

    internal void Popup()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + Math.Min(135, work.Height * 0.16);
        _statusText.Visibility = Visibility.Collapsed;
        _statusText.Text = "";
        _placeholder.Visibility = string.IsNullOrEmpty(_input.Text) ? Visibility.Visible : Visibility.Collapsed;
        Show();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            new ForegroundWindow().Activate(handle, Topmost);
        Activate();
        Keyboard.Focus(_input);
        _input.Focus();
        _input.SelectAll();
        InstallMouseHook();
        UpdateLiveResult();
    }

    internal void HideBar()
    {
        UninstallMouseHook();
        if (IsVisible) Hide();
    }

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, IntPtr.Zero, 0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsVisible && !_suppressDeactivate)
        {
            var msg = wParam.ToInt32();
            if (msg is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown)
            {
                var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
                var pt = info.Pt;
                if (!IsPointInsideCard(pt.X, pt.Y))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_suppressDeactivate && IsVisible) HideBar();
                    }));
                }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsPointInsideCard(int screenX, int screenY)
    {
        try
        {
            if (_shell.ActualWidth > 0 && _shell.ActualHeight > 0)
            {
                var topLeft = _shell.PointToScreen(new Point(0, 0));
                var bottomRight = _shell.PointToScreen(new Point(_shell.ActualWidth, _shell.ActualHeight));
                return screenX >= topLeft.X && screenX <= bottomRight.X
                    && screenY >= topLeft.Y && screenY <= bottomRight.Y;
            }
        }
        catch (InvalidOperationException) { }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
            return screenX >= rect.Left && screenX <= rect.Right && screenY >= rect.Top && screenY <= rect.Bottom;
        return true;
    }

    private void OpenInMain(string word)
    {
        HideBar();
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

        if (entry != null && !string.IsNullOrWhiteSpace(entry.DisplayMeaning))
        {
            _currentWord = entry.word;
            _wordText.Text = entry.word;
            _phoneticText.Text = string.IsNullOrWhiteSpace(entry.phonetic) ? "" : $"/{entry.phonetic.Trim('/')}/";
            _meaningText.Text = entry.DisplayMeaning;
            _speakBtn.Visibility = _bookmarkBtn.Visibility = Visibility.Visible;

            var note = _mnemonics.Get(entry.word, _accountKey()).FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(note))
            {
                _mnemonicText.Text = note.Length <= 150 ? note : note[..150] + "…";
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

            var fuzzy = _dictionary.SuggestSpellingCorrections(raw, 6);
            if (fuzzy.Count > 0)
            {
                _meaningText.Text = "未找到精确匹配，你是不是要查：";
                AddSuggestionPills(fuzzy);
            }
            else
            {
                _meaningText.Text = "按 Enter 在主窗口中继续查词。";
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
                FontSize = 12,
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 7, 5)
            };
            if (pillStyle != null) btn.Style = pillStyle;
            btn.Click += (_, _) => { _input.Text = word; _input.CaretIndex = word.Length; };
            _suggestionsPanel.Children.Add(btn);
            count++;
        }
        _suggestionsPanel.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MsllHookStruct { public NativePoint Pt; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);
}
