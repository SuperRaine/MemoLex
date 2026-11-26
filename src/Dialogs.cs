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
        panel = new StackPanel { Margin = new Thickness(26) };
        var dialog = new Window { Owner = owner?.IsLoaded==true?owner:null, Title = title + " · 墨墨搜词", Width = 470, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, ShowInTaskbar = false };
        WindowChrome.SetWindowChrome(dialog, new WindowChrome { CaptionHeight = 44, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
        RoundedWindow.Attach(dialog);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new Grid { Margin = new Thickness(12,0,6,0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/MemoLex;component/Assets/owl-logo.png")), Width = 29, Height = 29, Margin = new Thickness(0,0,8,0) });
        brand.Children.Add(new TextBlock { Text = title, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(75,101,123)), VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(brand);
        var close = new Button { Content = "\uE8BB", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Style = (Style)Application.Current.FindResource("WindowButton"), ToolTip = "关闭" };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_,_) => dialog.Close();
        Grid.SetColumn(close,1); bar.Children.Add(close);
        var titleBar = new Border { Child = bar, BorderBrush = new SolidColorBrush(Color.FromRgb(229,237,245)), BorderThickness = new Thickness(0,0,0,1) };
        layout.Children.Add(titleBar);
        var body = new ContentControl { Content = panel };
        Grid.SetRow(body,1); layout.Children.Add(body);
        dialog.Content = new Border { Child = layout, Tag = body, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(220,233,243)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
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
