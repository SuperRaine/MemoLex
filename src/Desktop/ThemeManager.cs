using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MemoLex.Desktop;

internal static class ThemeManager
{
    internal static bool IsDark { get; private set; }
    internal static string CurrentMode { get; private set; } = "light";
    internal static event Action<bool>? ThemeChanged;

    internal static bool ResolveIsDark(string? mode) => mode switch
    {
        "dark" => true,
        "system" => IsSystemDark(),
        _ => false
    };

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    internal static void Apply(string? mode)
    {
        CurrentMode = mode is "dark" or "system" ? mode : "light";
        var dark = ResolveIsDark(CurrentMode);
        IsDark = dark;

        var resources = Application.Current?.Resources;
        if (resources != null)
        {
            SetBrush(resources, "CanvasBrush", dark ? "#141D26" : "#F2F7FC");
            SetBrush(resources, "SurfaceBrush", dark ? "#1C2733" : "#FFFFFF");
            SetBrush(resources, "ChromeBrush", dark ? "#19232E" : "#FFFFFF");
            SetBrush(resources, "SidebarBrush", dark ? "#16202B" : "#F9FCFF");
            SetBrush(resources, "ControlBgBrush", dark ? "#22303E" : "#FFFFFF");
            SetBrush(resources, "CardSoftBrush", dark ? "#1D2C3A" : "#F4FAFF");
            SetBrush(resources, "CardAltBrush", dark ? "#1A2633" : "#F9FCFF");
            SetBrush(resources, "AccentWashBrush", dark ? "#16384E" : "#EAF7FF");
            SetBrush(resources, "AccentDeepBrush", dark ? "#5EC8FA" : "#0A7FB3");
            SetBrush(resources, "InkBrush", dark ? "#E6F1F8" : "#132C40");
            SetBrush(resources, "InkStrongBrush", dark ? "#F4FAFF" : "#0B2234");
            SetBrush(resources, "MutedBrush", dark ? "#9AB3C7" : "#607C93");
            SetBrush(resources, "SubtleBrush", dark ? "#7A95AA" : "#8CA5B9");
            SetBrush(resources, "BorderBrush", dark ? "#2C4052" : "#D9E8F4");
            SetBrush(resources, "RuleBrush", dark ? "#253646" : "#EAF1F7");
            SetBrush(resources, "PillBgBrush", dark ? "#1A374A" : "#EDF7FF");
            SetBrush(resources, "PillBorderBrush", dark ? "#2A536F" : "#CEE7F9");
            SetBrush(resources, "HoverBgBrush", dark ? "#23394D" : "#EDF7FF");
            SetBrush(resources, "SelectedBgBrush", dark ? "#1D445E" : "#E1F3FF");
            SetBrush(resources, "WarnBgBrush", dark ? "#332614" : "#FFF7E6");
            SetBrush(resources, "WarnBorderBrush", dark ? "#6E4F1D" : "#F4CF86");
            SetBrush(resources, "WarnFgBrush", dark ? "#F8C76A" : "#9A5207");
        }

        if (Application.Current != null)
        {
            foreach (Window window in Application.Current.Windows)
                ApplyWindowFrame(window, dark);
        }

        ThemeChanged?.Invoke(dark);
    }

    internal static void ApplyWindowFrame(Window window, bool? darkOverride = null)
    {
        var dark = darkOverride ?? IsDark;
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var useDark = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int));
        }
        catch { }
    }

    internal static Brush Res(string key, string lightHex, string darkHex)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        return Hex(IsDark ? darkHex : lightHex);
    }

    internal static SolidColorBrush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        resources[key] = brush;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
