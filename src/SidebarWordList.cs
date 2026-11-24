using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MemoLex;

public sealed class SidebarWordList : ListBox
{
    private ScrollViewer? _scroll;
    private int _wheelRemainder;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _scroll = FindScrollViewer(this);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Handled || e.Delta == 0 || _scroll is not { CanContentScroll: true, ScrollableHeight: > 0 } scroll
            || scroll.ViewportHeight >= 3)
        {
            _wheelRemainder = 0;
            return;
        }

        _wheelRemainder += e.Delta;
        var page = Math.Max(1, (int)scroll.ViewportHeight);
        var notches = _wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        _wheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        if (notches != 0) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - page * notches);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var scroll = child as ScrollViewer ?? FindScrollViewer(child);
            if (scroll != null) return scroll;
        }
        return null;
    }
}
