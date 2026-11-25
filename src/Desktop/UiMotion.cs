using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MemoLex.Desktop;

internal static class UiMotion
{
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(260);
    private static readonly QuinticEase EaseOut = CreateQuinticEase();
    private static readonly CubicEase CubicEaseOut = CreateCubicEase(EasingMode.EaseOut);
    private static readonly CubicEase CubicEaseIn = CreateCubicEase(EasingMode.EaseIn);

    public static bool Enabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    public static void Reveal(FrameworkElement element, double distance = 6)
    {
        if (!element.IsVisible || !Enabled)
            return;

        double opacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        element.BeginAnimation(UIElement.OpacityProperty, Animation(opacity * 0.45, opacity, RevealDuration));
        if (distance == 0)
            return;
        var translation = EnsureTranslateTransform(element);
        if (translation != null)
        {
            double offset = (double)translation.GetAnimationBaseValue(TranslateTransform.YProperty);
            translation.BeginAnimation(TranslateTransform.YProperty, Animation(offset + distance, offset, RevealDuration));
        }
    }

    public static void SlideFade(FrameworkElement element, double xOffset, double yOffset = 0, int durationMs = 220)
    {
        if (!element.IsVisible || !Enabled)
            return;

        var duration = TimeSpan.FromMilliseconds(durationMs);
        double opacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        element.BeginAnimation(UIElement.OpacityProperty, Animation(opacity * 0.25, opacity, duration));
        var translation = EnsureTranslateTransform(element);
        if (translation == null)
            return;
        if (xOffset != 0)
        {
            double baseX = (double)translation.GetAnimationBaseValue(TranslateTransform.XProperty);
            translation.BeginAnimation(TranslateTransform.XProperty, Animation(baseX + xOffset, baseX, duration));
        }
        if (yOffset != 0)
        {
            double baseY = (double)translation.GetAnimationBaseValue(TranslateTransform.YProperty);
            translation.BeginAnimation(TranslateTransform.YProperty, Animation(baseY + yOffset, baseY, duration));
        }
    }

    public static void Pop(FrameworkElement element, double peakScale = 1.045, int durationMs = 210)
    {
        if (!element.IsVisible || !Enabled)
            return;

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = EnsureScaleTransform(element);
        if (scale == null)
            return;

        var half = TimeSpan.FromMilliseconds(durationMs / 2.0);
        var upX = new DoubleAnimation(1.0, peakScale, half) { EasingFunction = CubicEaseOut, AutoReverse = true, FillBehavior = FillBehavior.Stop };
        var upY = new DoubleAnimation(1.0, peakScale, half) { EasingFunction = CubicEaseOut, AutoReverse = true, FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, upX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, upY);
    }

    public static void SwipeOut(FrameworkElement element, double toX, double tiltDegrees, int durationMs, Action onCompleted)
    {
        if (!element.IsVisible || !Enabled)
        {
            onCompleted();
            return;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var translate = new TranslateTransform();
        var rotate = new RotateTransform();
        var group = new TransformGroup();
        group.Children.Add(rotate);
        group.Children.Add(translate);
        element.RenderTransform = group;

        var duration = TimeSpan.FromMilliseconds(durationMs);
        var moveX = new DoubleAnimation(0, toX, duration) { EasingFunction = CubicEaseIn };
        var tilt = new DoubleAnimation(0, tiltDegrees, duration) { EasingFunction = CubicEaseIn };
        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = CubicEaseIn };
        moveX.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            onCompleted();
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        rotate.BeginAnimation(RotateTransform.AngleProperty, tilt);
        translate.BeginAnimation(TranslateTransform.XProperty, moveX);
    }

    public static void AnimateWidth(FrameworkElement element, double toWidth, int durationMs = 260)
    {
        var target = Math.Max(0, toWidth);
        if (!Enabled || !element.IsLoaded)
        {
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            element.Width = target;
            return;
        }

        var from = double.IsNaN(element.Width) ? element.ActualWidth : element.Width;
        var anim = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = EaseOut
        };
        element.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private static TranslateTransform? EnsureTranslateTransform(FrameworkElement element)
    {
        if (ReferenceEquals(element.RenderTransform, Transform.Identity) || element.RenderTransform == null)
        {
            var created = new TranslateTransform();
            element.RenderTransform = created;
            return created;
        }
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } direct)
            return direct;
        if (element.RenderTransform is TransformGroup { IsFrozen: false } group)
        {
            foreach (var child in group.Children)
                if (child is TranslateTransform { IsFrozen: false } existing)
                    return existing;
            var added = new TranslateTransform();
            group.Children.Add(added);
            return added;
        }
        return null;
    }

    private static ScaleTransform? EnsureScaleTransform(FrameworkElement element)
    {
        if (ReferenceEquals(element.RenderTransform, Transform.Identity) || element.RenderTransform == null)
        {
            var created = new ScaleTransform(1, 1);
            element.RenderTransform = created;
            return created;
        }
        if (element.RenderTransform is ScaleTransform { IsFrozen: false } direct)
            return direct;
        if (element.RenderTransform is TransformGroup { IsFrozen: false } group)
        {
            foreach (var child in group.Children)
                if (child is ScaleTransform { IsFrozen: false } existing)
                    return existing;
            var added = new ScaleTransform(1, 1);
            group.Children.Insert(0, added);
            return added;
        }
        var replacement = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        replacement.Children.Add(scale);
        replacement.Children.Add(element.RenderTransform);
        element.RenderTransform = replacement;
        return scale;
    }

    private static DoubleAnimation Animation(double from, double to, TimeSpan duration) =>
        new(from, to, duration) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };

    private static QuinticEase CreateQuinticEase()
    {
        var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
        easing.Freeze();
        return easing;
    }

    private static CubicEase CreateCubicEase(EasingMode mode)
    {
        var easing = new CubicEase { EasingMode = mode };
        easing.Freeze();
        return easing;
    }
}
