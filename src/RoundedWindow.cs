using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MemoLex.Desktop;

namespace MemoLex;

internal static class RoundedWindow
{
    internal const double Radius = 12;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmWindowPosChanging = 0x0046;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int SwpNoSize = 0x0001;

    internal static void Attach(Window window)
    {
        var nativeRounded = false;
        var resizing = false;
        var hasRendered = false;
        var hasPrevRect = false;
        var resizePending = false;
        var prevRect = default(NativeRect);
        var backgroundBrush = IntPtr.Zero;
        uint backgroundColorRef = uint.MaxValue;
        HwndSource? attachedSource = null;

        Color ResolveBackdropColor()
        {
            if (window.Background is SolidColorBrush solid && solid.Color.A == 255)
                return solid.Color;
            return ThemeManager.IsDark
                ? Color.FromRgb(0x14, 0x1D, 0x26)
                : Color.FromRgb(0xF2, 0xF7, 0xFC);
        }

        IntPtr EnsureBackgroundBrush()
        {
            var c = ResolveBackdropColor();
            var colorRef = (uint)((c.B << 16) | (c.G << 8) | c.R);
            if (backgroundBrush != IntPtr.Zero && backgroundColorRef == colorRef)
                return backgroundBrush;
            if (backgroundBrush != IntPtr.Zero)
                DeleteObject(backgroundBrush);
            backgroundBrush = CreateSolidBrush(colorRef);
            backgroundColorRef = colorRef;
            return backgroundBrush;
        }

        void SyncCompositionTarget(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            var source = attachedSource ?? HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not { } target) return;
            // Per-window software rendering avoids D3D9Ex swap-chain recreation stalls (~100-230ms/step
            // on Windows 11 hybrid/high-refresh GPUs) during live resize, speeding drag-resize up ~10x.
            if (target.RenderMode != RenderMode.SoftwareOnly)
                target.RenderMode = RenderMode.SoftwareOnly;
            target.BackgroundColor = ResolveBackdropColor();
        }

        void ApplyChromeRadius()
        {
            if (WindowChrome.GetWindowChrome(window) is not { } chrome) return;
            // On Windows 11, DWMWA_WINDOW_CORNER_PREFERENCE handles hardware rounded corners natively.
            // Setting WindowChrome.CornerRadius != 0 with GlassFrameThickness == 0 causes WPF to call
            // SetWindowRgn(..., fRedraw: true) on every single WM_SIZE drag pixel, causing severe twitching.
            if (nativeRounded)
            {
                chrome.CornerRadius = new CornerRadius(0);
                if (chrome.GlassFrameThickness == default)
                    chrome.GlassFrameThickness = new Thickness(0, 0, 0, 1);
            }
            else
            {
                chrome.CornerRadius = new CornerRadius(window.WindowState == WindowState.Maximized || resizing ? 0 : Radius);
            }
        }

        void Update()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                var preference = window.WindowState == WindowState.Maximized ? 1 : 2;
                nativeRounded = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int)) == 0;
                if (nativeRounded)
                    SetWindowRgn(handle, IntPtr.Zero, false);
            }
            ApplyChromeRadius();
            SyncCompositionTarget(handle);
        }

        void OnThemeChanged(bool _)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            EnsureBackgroundBrush();
            SyncCompositionTarget(handle);
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmWindowPosChanging)
            {
                if (GetWindowRect(hwnd, out var current))
                {
                    prevRect = current;
                    hasPrevRect = true;
                }
                if (lParam != IntPtr.Zero)
                {
                    var flags = Marshal.ReadInt32(lParam, IntPtr.Size * 2 + 16);
                    resizePending = (flags & SwpNoSize) == 0;
                }
                return IntPtr.Zero;
            }
            if (msg == WmWindowPosChanged)
            {
                resizePending = false;
                if (GetWindowRect(hwnd, out var current))
                {
                    prevRect = current;
                    hasPrevRect = true;
                }
                return IntPtr.Zero;
            }
            if (msg == WmEraseBkgnd)
            {
                if (wParam != IntPtr.Zero)
                {
                    var brush = EnsureBackgroundBrush();
                    if (brush != IntPtr.Zero && GetClientRect(hwnd, out var client))
                    {
                        if (!hasRendered)
                        {
                            FillRect(wParam, ref client, brush);
                        }
                        else if (resizePending && hasPrevRect && GetWindowRect(hwnd, out var cur))
                        {
                            var prevW = prevRect.Right - prevRect.Left;
                            var prevH = prevRect.Bottom - prevRect.Top;
                            if (prevW <= 0 || prevH <= 0 || prevRect.Left <= -10000 || prevRect.Top <= -10000)
                            {
                                FillRect(wParam, ref client, brush);
                            }
                            else
                            {
                                // Paint only newly exposed outer strips so existing rendered pixels never flash blank.
                                var w = client.Right;
                                var h = client.Bottom;
                                if (cur.Left < prevRect.Left)
                                {
                                    var strip = new NativeRect { Left = 0, Top = 0, Right = Math.Min(w, prevRect.Left - cur.Left), Bottom = h };
                                    if (strip.Right > 0) FillRect(wParam, ref strip, brush);
                                }
                                if (cur.Top < prevRect.Top)
                                {
                                    var strip = new NativeRect { Left = 0, Top = 0, Right = w, Bottom = Math.Min(h, prevRect.Top - cur.Top) };
                                    if (strip.Bottom > 0) FillRect(wParam, ref strip, brush);
                                }
                                if (cur.Right > prevRect.Right)
                                {
                                    var left = Math.Max(0, prevRect.Right - cur.Left);
                                    var strip = new NativeRect { Left = left, Top = 0, Right = w, Bottom = h };
                                    if (strip.Right > strip.Left) FillRect(wParam, ref strip, brush);
                                }
                                if (cur.Bottom > prevRect.Bottom)
                                {
                                    var top = Math.Max(0, prevRect.Bottom - cur.Top);
                                    var strip = new NativeRect { Left = 0, Top = top, Right = w, Bottom = h };
                                    if (strip.Bottom > strip.Top) FillRect(wParam, ref strip, brush);
                                }
                            }
                        }
                    }
                }
                handled = true;
                return new IntPtr(1);
            }
            if (!nativeRounded)
            {
                if (msg == WmEnterSizeMove) { resizing = true; ApplyChromeRadius(); }
                else if (msg == WmExitSizeMove) { resizing = false; ApplyChromeRadius(); }
            }
            return IntPtr.Zero;
        }

        Update();
        ThemeManager.ThemeChanged += OnThemeChanged;
        window.ContentRendered += (_, _) => hasRendered = true;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            attachedSource = HwndSource.FromHwnd(handle);
            Update();
            EnsureBackgroundBrush();
            attachedSource?.AddHook(Hook);
            // WindowChromeWorker._ExtendGlassFrame can overwrite BackgroundColor to Transparent;
            // re-sync once loaded so the composition backdrop stays matched to the active theme.
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SyncCompositionTarget(handle)));
        };
        window.StateChanged += (_, _) => Update();
        window.Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            attachedSource?.RemoveHook(Hook);
            attachedSource = null;
            if (backgroundBrush != IntPtr.Zero)
            {
                DeleteObject(backgroundBrush);
                backgroundBrush = IntPtr.Zero;
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint colorRef);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
}
