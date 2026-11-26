using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace MemoLex;

internal static class RoundedWindow
{
    internal const double Radius = 12;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    internal static void Attach(Window window)
    {
        var nativeRounded = false;
        var resizing = false;

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
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmEraseBkgnd)
            {
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
        window.SourceInitialized += (_, _) =>
        {
            Update();
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
        };
        window.StateChanged += (_, _) => Update();
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
}
