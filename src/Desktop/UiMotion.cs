using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MemoLex.Desktop;

internal static class UiMotion
{
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(250);
    private static readonly QuinticEase EaseOut = CreateEasing();

    public static void Reveal(FrameworkElement element, double distance = 5)
    {
        if (!element.IsVisible || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            return;

        double opacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        element.BeginAnimation(UIElement.OpacityProperty, Animation(opacity * 0.65, opacity));
        if (distance == 0)
            return;
        if (ReferenceEquals(element.RenderTransform, Transform.Identity))
            element.RenderTransform = new TranslateTransform();
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } translation)
        {
            double offset = (double)translation.GetAnimationBaseValue(TranslateTransform.YProperty);
            translation.BeginAnimation(TranslateTransform.YProperty, Animation(offset + distance, offset));
        }
    }

    private static DoubleAnimation Animation(double from, double to) =>
        new(from, to, RevealDuration) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };

    private static QuinticEase CreateEasing()
    {
        var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
        easing.Freeze();
        return easing;
    }
}
