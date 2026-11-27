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
    private static readonly SineEase SineEaseIn = CreateSineEase(EasingMode.EaseIn);

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

    public static void Flip(FrameworkElement element, Action onMidpoint, Action onCompleted, int firstHalfMs = 95, int secondHalfMs = 135)
    {
        if (!element.IsVisible || !Enabled)
        {
            onMidpoint();
            onCompleted();
            return;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        element.RenderTransform = scale;
        var previousCache = element.CacheMode;
        element.CacheMode = new BitmapCache { RenderAtScale = 1 };

        var firstDuration = TimeSpan.FromMilliseconds(firstHalfMs);
        var shrinkX = new DoubleAnimation(1.0, 0.02, firstDuration) { EasingFunction = SineEaseIn };
        var shrinkY = new DoubleAnimation(1.0, 0.975, firstDuration) { EasingFunction = SineEaseIn };
        var dim = new DoubleAnimation(1.0, 0.86, firstDuration) { EasingFunction = SineEaseIn };

        shrinkX.Completed += (_, _) =>
        {
            element.CacheMode = previousCache;
            onMidpoint();
            if (!element.IsVisible || !element.IsLoaded)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                element.RenderTransform = Transform.Identity;
                onCompleted();
                return;
            }
            element.UpdateLayout();
            element.CacheMode = new BitmapCache { RenderAtScale = 1 };

            var secondDuration = TimeSpan.FromMilliseconds(secondHalfMs);
            var expandX = new DoubleAnimation(0.02, 1.0, secondDuration) { EasingFunction = CubicEaseOut };
            var expandY = new DoubleAnimation(0.975, 1.0, secondDuration) { EasingFunction = CubicEaseOut };
            var brighten = new DoubleAnimation(0.86, 1.0, secondDuration) { EasingFunction = CubicEaseOut };

            expandX.Completed += (_, _) =>
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                element.RenderTransform = Transform.Identity;
                element.CacheMode = previousCache;
                onCompleted();
            };

            element.BeginAnimation(UIElement.OpacityProperty, brighten);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, expandX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, expandY);
        };

        element.BeginAnimation(UIElement.OpacityProperty, dim);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
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
        var previousCache = element.CacheMode;
        element.CacheMode = new BitmapCache { RenderAtScale = 1 };

        var duration = TimeSpan.FromMilliseconds(durationMs);
        var moveX = new DoubleAnimation(0, toX, duration) { EasingFunction = CubicEaseIn };
        var tilt = new DoubleAnimation(0, tiltDegrees, duration) { EasingFunction = CubicEaseIn };
        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = CubicEaseIn };
        moveX.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            element.CacheMode = previousCache;
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
        if (Math.Abs(from - target) < 0.5)
        {
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            element.Width = target;
            return;
        }
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

    private static SineEase CreateSineEase(EasingMode mode)
    {
        var easing = new SineEase { EasingMode = mode };
        easing.Freeze();
        return easing;
    }
}
