using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Input;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;
internal static class InputDialog
{
    public static string? Ask(Window owner, string title, string hint, string initial, string confirm)
    {
        var dialog = Shell(owner, title, out var panel);
        panel.Children.Add(Text(hint));
        var input = new TextBox { Text = initial, FontSize = 15, Padding = new Thickness(9), Margin = new Thickness(0, 16, 0, 18), MaxLength = 100 };
        panel.Children.Add(input);
        var button = new Button { Content = confirm, Padding = new Thickness(18, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        button.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; };
        panel.Children.Add(button);
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    public static bool Confirm(Window owner, string title, string message, string confirmLabel, string cancelLabel = "取消")
    {
        var dialog = Shell(owner, title, out var panel);
        dialog.Width = 420;
        panel.Children.Add(Text(message));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = cancelLabel, Padding = new Thickness(15, 7, 15, 7), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = confirmLabel, Padding = new Thickness(16, 7, 16, 7), Style = (Style)Application.Current.FindResource("AccentButton"), IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }
    internal static Window Shell(Window owner, string title, out StackPanel panel)
    {
        panel = new StackPanel { Margin = new Thickness(26, 22, 26, 24) };
        var dialog = new Window { Owner = owner?.IsLoaded==true?owner:null, Title = title + " · 墨墨搜词", Width = 470, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, ShowInTaskbar = false };
        WindowChrome.SetWindowChrome(dialog, new WindowChrome { CaptionHeight = 46, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
        RoundedWindow.Attach(dialog);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new Grid { Margin = new Thickness(14,0,6,0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logoBadge = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Margin = new Thickness(0,0,9,0),
            Background = new SolidColorBrush(Color.FromRgb(237,247,255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(210,232,248)),
            BorderThickness = new Thickness(1), Padding = new Thickness(2),
            Child = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/MemoLex;component/Assets/owl-logo.png")), Stretch = Stretch.Uniform }
        };
        RenderOptions.SetBitmapScalingMode(logoBadge.Child, BitmapScalingMode.HighQuality);
        brand.Children.Add(logoBadge);
        brand.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(26,58,82)), VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(brand);
        var close = new Button { Content = "\uE8BB", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11.5, Style = (Style)Application.Current.FindResource("CloseWindowButton"), ToolTip = "关闭" };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_,_) => dialog.Close();
        Grid.SetColumn(close,1); bar.Children.Add(close);
        var titleBar = new Border
        {
            Child = bar, Background = new SolidColorBrush(Color.FromRgb(249,252,255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226,237,246)), BorderThickness = new Thickness(0,0,0,1),
            CornerRadius = new CornerRadius(11,11,0,0)
        };
        layout.Children.Add(titleBar);
        var body = new ContentControl { Content = panel };
        Grid.SetRow(body,1); layout.Children.Add(body);
        dialog.Content = new Border { Child = layout, Tag = body, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(212,229,242)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        dialog.PreviewKeyDown += (_,e) => { if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); } };
        NameScope.SetNameScope(dialog,new NameScope());
        return dialog;
    }
    internal static void SetBody(Window dialog, UIElement content) => ((ContentControl)((Border)dialog.Content).Tag).Content = content;
    internal static TextBlock Text(string text, bool status = false)
    {
        var label = status ? new StatusLabel() : new TextBlock();
        label.Text = text;
        label.TextWrapping = TextWrapping.Wrap;
        label.LineHeight = 23;
        label.Foreground = new SolidColorBrush(Color.FromRgb(100,126,147));
        return label;
    }
}
