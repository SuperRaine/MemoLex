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
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, ShowInTaskbar = false };
        dialog.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        WindowChrome.SetWindowChrome(dialog, new WindowChrome { CaptionHeight = 46, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
        RoundedWindow.Attach(dialog);
        dialog.SourceInitialized += (_, _) => Desktop.ThemeManager.ApplyWindowFrame(dialog);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new Grid { Margin = new Thickness(14,0,6,0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = new Image
        {
            Width = 24, Height = 24, Margin = new Thickness(0,0,8,0),
            Source = new BitmapImage(new Uri("pack://application:,,,/MemoLex;component/Assets/owl-logo.png")),
            Stretch = Stretch.Uniform
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        brand.Children.Add(logo);
        var titleBlock = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        brand.Children.Add(titleBlock);
        bar.Children.Add(brand);
        var close = new Button { Content = "\uE8BB", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11.5, Style = (Style)Application.Current.FindResource("CloseWindowButton"), ToolTip = "关闭" };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_,_) => dialog.Close();
        Grid.SetColumn(close,1); bar.Children.Add(close);
        var titleBar = new Border
        {
            Child = bar,
            BorderThickness = new Thickness(0,0,0,1),
            CornerRadius = new CornerRadius(11,11,0,0)
        };
        titleBar.SetResourceReference(Border.BackgroundProperty, "ChromeBrush");
        titleBar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        layout.Children.Add(titleBar);
        var body = new ContentControl { Content = panel };
        Grid.SetRow(body,1); layout.Children.Add(body);
        var outer = new Border { Child = layout, Tag = body, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        outer.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        outer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        dialog.Content = outer;
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
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return label;
    }
}
