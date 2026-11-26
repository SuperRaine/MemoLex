using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal readonly record struct HoverBookmark(int Account, string Book, string Title, bool Saved, bool Pending)
{
    internal bool Available => Book.Length > 0;
}

internal sealed class HoverLookupWindow : Window
{
    private readonly TextBlock _word = new() { FontSize = 21, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x22, 0x34)), TextWrapping = TextWrapping.Wrap, MaxHeight = 60, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _ipa = new() { FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0x6D, 0x89, 0x9E)), Margin = new Thickness(8, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _notice = new() { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x96, 0xAB)), Margin = new Thickness(0, 3, 0, 6), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock _meaning = new() { FontSize = 13.5, LineHeight = 21, Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x34, 0x4A)), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _mnemonic = new() { FontSize = 12, LineHeight = 18.5, Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x54, 0x73)), TextWrapping = TextWrapping.Wrap };
    private readonly Border _mnemonicCard;
    private readonly Border _shell;
    private readonly TextBlock _bookmarkHint = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x81, 0x98)), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
    private readonly Button _bookmarkButton = new() { Content = "＋ 收藏", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(11, 5, 11, 5), FontSize = 11.5, FontWeight = FontWeights.SemiBold };
    private readonly Button _speakButton = new() { Content = "\uE767", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(7, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "播放单词发音" };
    private readonly Func<string, HoverBookmark> _bookmarkStatus;
    private readonly Func<string, HoverBookmark, Task<string?>> _save;
    private readonly Func<string, Task>? _speak;
    private HoverBookmark _bookmark;
    private int _presentation;
    internal event Action<string>? OpenWord;
    internal event Action? Dismissed;
    private string _currentWord = "";

    internal HoverLookupWindow(Func<string, HoverBookmark> bookmarkStatus, Func<string, HoverBookmark, Task<string?>> save, Func<string, Task>? speak = null)
    {
        _bookmarkStatus = bookmarkStatus; _save = save; _speak = speak;
        Width = 376; MaxHeight = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;

        var content = new StackPanel { Margin = new Thickness(16, 13, 16, 13) };

        var titleBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var close = new Button { Content = "\uE8BB", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Width = 24, Height = 24, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x95, 0xAA)), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(close, Dock.Right);
        close.Click += (_, _) => { Hide(); Dismissed?.Invoke(); };
        titleBar.Children.Add(close);

        var wordRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        wordRow.Children.Add(_word);
        wordRow.Children.Add(_ipa);
        if (Application.Current.TryFindResource("PillButton") is Style pillStyle) _speakButton.Style = pillStyle;
        _speakButton.Visibility = _speak == null ? Visibility.Collapsed : Visibility.Visible;
        _speakButton.Click += async (_, _) =>
        {
            if (_speak == null || string.IsNullOrWhiteSpace(_currentWord)) return;
            _speakButton.IsEnabled = false;
            try { await _speak(_currentWord); }
            finally { _speakButton.IsEnabled = true; }
        };
        wordRow.Children.Add(_speakButton);
        titleBar.Children.Add(wordRow);
        content.Children.Add(titleBar);
        content.Children.Add(_notice);

        var definitions = new StackPanel();
        definitions.Children.Add(_meaning);

        var mnemonicRow = new Grid();
        mnemonicRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mnemonicRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var noteBadge = new Border
        {
            Width = 19, Height = 19, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(0xD8, 0xF0, 0xFE)),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 7, 0),
            Child = new TextBlock { Text = "记", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x7F, 0xB3)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        mnemonicRow.Children.Add(noteBadge);
        Grid.SetColumn(_mnemonic, 1);
        mnemonicRow.Children.Add(_mnemonic);

        _mnemonicCard = new Border
        {
            Child = mnemonicRow, Margin = new Thickness(0, 9, 0, 0), Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF9, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xEC, 0xFB)),
            BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed
        };
        definitions.Children.Add(_mnemonicCard);
        content.Children.Add(new ScrollViewer { Content = definitions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 205 });

        var footerBar = new Border
        {
            Margin = new Thickness(0, 11, 0, 0), Padding = new Thickness(0, 9, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF1, 0xFA)), BorderThickness = new Thickness(0, 1, 0, 0)
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_bookmarkButton);
        var open = new Button { Content = "查看完整释义 →", Padding = new Thickness(12, 5, 12, 5), FontSize = 11.5 };
        if (Application.Current.TryFindResource("AccentButton") is Style accentStyle) open.Style = accentStyle;
        open.Click += (_, _) => OpenWord?.Invoke(_currentWord);
        actions.Children.Add(open);
        footerBar.Child = actions;
        content.Children.Add(footerBar);
        content.Children.Add(_bookmarkHint);

        _bookmarkButton.Click += async (_, _) =>
        {
            var presentation = _presentation; var word = _currentWord; var target = _bookmarkStatus(word);
            if (!target.Available || target.Saved || target.Pending || target != _bookmark) { UpdateBookmark(); return; }
            SetHint("");
            var saveTask = _save(word, target);
            UpdateBookmark();
            var error = await saveTask;
            if (presentation != _presentation || !IsVisible) return;
            UpdateBookmark();
            var current = _bookmarkStatus(word);
            if (current.Account == target.Account && current.Book == target.Book)
                SetHint(error ?? (current.Saved ? $"已收藏到「{current.Title}」" : ""));
        };

        _shell = new Border
        {
            Child = content, CornerRadius = new CornerRadius(14), Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9D, 0xD5, 0xF7)), BorderThickness = new Thickness(1),
            Margin = new Thickness(10, 8, 10, 14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = 0.16, Color = Color.FromRgb(0x0E, 0x48, 0x70) }
        };
        Content = _shell;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle; var style = GetWindowLongPtr(hwnd, -20);
            SetWindowLongPtr(hwnd, -20, new IntPtr(style.ToInt64() | 0x08000000 | 0x00000080));
        };
    }

    private void SetHint(string text)
    {
        _bookmarkHint.Text = text;
        _bookmarkHint.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        _bookmarkHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void Present(HoverWord hit, DictionaryEntry entry, IReadOnlyList<StoredMnemonic> notes)
    {
        SetContent(hit, entry, notes);
        var opacity = Opacity; Opacity = 0;
        if (!IsVisible) { Left = -32000; Top = -32000; Show(); }
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle; var dpi = GetDpiForWindow(hwnd) / 96d;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)hit.Bounds.X, (int)hit.Bounds.Y)).WorkingArea;
        var width = (int)Math.Ceiling(ActualWidth * dpi); var height = (int)Math.Ceiling(ActualHeight * dpi);
        var x = Math.Clamp((int)hit.Bounds.X, screen.Left, Math.Max(screen.Left, screen.Right - width));
        var y = (int)(hit.Bounds.Y + hit.Bounds.Height + 7);
        if (y + height > screen.Bottom) y = Math.Max(screen.Top, (int)hit.Bounds.Y - height - 7);
        SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0, 0x0010 | 0x0001);
        Opacity = opacity;
        UiMotion.Reveal(_shell, distance: 5);
    }

    internal void SetContent(HoverWord hit, DictionaryEntry entry, IReadOnlyList<StoredMnemonic> notes)
    {
        _presentation++; SetHint("");
        _currentWord = entry.word; _word.Text = entry.word;
        _ipa.Text = string.IsNullOrWhiteSpace(entry.phonetic) ? "" : $"/{entry.phonetic.Trim('/')}/";
        _ipa.Visibility = string.IsNullOrEmpty(_ipa.Text) ? Visibility.Collapsed : Visibility.Visible;
        var exact = entry.word.Equals(hit.Word, StringComparison.OrdinalIgnoreCase);
        _notice.Text = exact ? "" : $"未找到 “{hit.Word}”，已匹配相近词：{entry.word}";
        _notice.Visibility = exact ? Visibility.Collapsed : Visibility.Visible;
        UpdateBookmark();
        var meaning = entry.DisplayMeaning;
        _meaning.Text = meaning.Length <= 600 ? meaning : meaning[..600] + "…";
        var noteText = notes.FirstOrDefault()?.Text ?? "";
        if (noteText.Length > 220) noteText = noteText[..220] + "…";
        _mnemonic.Text = noteText;
        _mnemonicCard.Visibility = noteText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void UpdateBookmark()
    {
        var status = _bookmarkStatus(_currentWord);
        if (status.Account != _bookmark.Account || status.Book != _bookmark.Book) SetHint("");
        _bookmark = status;
        _bookmarkButton.Content = status.Pending ? "收藏中…" : status.Saved ? "✓ 已收藏" : "＋ 收藏";
        _bookmarkButton.IsEnabled = status.Available && !status.Pending && !status.Saved;
        if (status.Saved)
        {
            _bookmarkButton.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xFA, 0xF2));
            _bookmarkButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0xDC, 0xB9));
            _bookmarkButton.Foreground = new SolidColorBrush(Color.FromRgb(0x0E, 0x7C, 0x55));
        }
        else
        {
            _bookmarkButton.Background = Brushes.White;
            _bookmarkButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xE5, 0xF7));
            _bookmarkButton.Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x7F, 0xB3));
        }
        _bookmarkButton.ToolTip = status.Available ? $"加入「{status.Title}」" : status.Title;
        ToolTipService.SetShowOnDisabled(_bookmarkButton, true);
    }

    internal void ApplyAppearance(Preferences preferences)
    {
        Opacity = preferences.HoverTranslucent ? 0.90 : 1;
        var scale = preferences.HoverFontSize switch { "small" => 0.88, "large" => 1.16, _ => 1d };
        _word.FontSize = 21 * scale; _meaning.FontSize = 13.5 * scale; _meaning.LineHeight = 21 * scale; _mnemonic.FontSize = 12 * scale;
    }

    internal ScreenBox ScreenBounds
    {
        get { if (!IsVisible || !GetWindowRect(new WindowInteropHelper(this).Handle, out var r)) return default; return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
}
