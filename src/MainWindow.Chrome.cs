using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private bool? _compactLayout;
    private void UpdateResponsiveLayout(Size size)
    {
        var compact=size.Width<950;
        if(_compactLayout!=compact)
        {
            _compactLayout=compact;
            SidebarColumn.Width=new GridLength(compact?220:264);
            BrandColumn.Width=GridLength.Auto;
            BrandGroup.Margin=new Thickness(0,0,compact?6:12,0);
            BrandLogo.Width=BrandLogo.Height=compact?34:42;BrandTitleText.FontSize=compact?15:17;
            BackButton.Width=ForwardButton.Width=compact?27:32;
            MinimizeWindowButton.Width=MaximizeButton.Width=CloseWindowButton.Width=compact?30:44;
            SettingsButton.Width=PinButton.Width=compact?28:34;
            SettingsButton.Margin=new Thickness(0,0,compact?0:5,0);
            PinButton.Margin=new Thickness(0,0,compact?2:6,0);
            WindowControlsSeparator.Margin=new Thickness(0,0,compact?3:7,0);
            AccountButton.Margin=new Thickness(0,0,compact?3:5,0);
            ExamCountdownText.Margin=new Thickness(0,0,compact?8:14,0);
            HeaderCenterGrid.Margin=compact?new Thickness(6,0,6,0):new Thickness(9,0,10,0);
            CloudStatusText.MaxWidth=compact?72:100;
            AccountButton.MaxWidth=compact?115:145;
            AccountButton.Padding=compact?new Thickness(8,7,8,7):new Thickness(11,7,11,7);
            ExamCountdownText.MaxWidth=compact?120:210;
            MainContentGrid.Margin=compact?new Thickness(15,15,15,13):new Thickness(27,21,27,17);
            WordCard.Padding=compact?new Thickness(18):new Thickness(27,23,27,24);
            WordText.FontSize=compact?32:42;
            WordActions.Orientation=compact?System.Windows.Controls.Orientation.Vertical:System.Windows.Controls.Orientation.Horizontal;
            AddTodayButton.Margin=compact?new Thickness(0,6,0,0):new Thickness(8,0,0,0);
            AddTodayButton.Padding=compact?new Thickness(10,7,10,7):new Thickness(12,11,12,11);
        }
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void UpdateWindowChrome()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        RootFrame.CornerRadius = new CornerRadius(maximized ? 0 : 11);
    }
    private void UpdatePinAppearance()
    {
        PinButton.Background = Topmost ? Brush("#E2F5FF") : Brushes.Transparent;
        PinButton.Foreground = Topmost ? Brush("#087FAF") : Brush("#6F8498");
        PinButton.ToolTip = Topmost ? "取消置顶" : "窗口置顶";
    }
    private void SetAccountStatus(string status)
    {
        var hasToken=_tokens.HasToken;
        if(!hasToken&&_tokens.HasExpiredSession)status=InactiveAccountStatus;
        if(status=="已连接")status=_accountRejected?"登录失效":_accountAuthenticated?"已连接":"连接中";
        var accountName=_tokens.ActiveAccount?.Name;
        CloudStatusText.Text = hasToken&&!string.IsNullOrWhiteSpace(accountName)?accountName:status=="未连接"?"未登录":status;
        CloudStatusText.Foreground = Brush(status == "已连接" ? "#087FAF" : status is "登录已失效" or "登录失效" or "连接受限" ? "#B58236" : "#8399AA");
        AccountButton.ToolTip = $"墨墨账号：{accountName??"未选择账号"} · {status}\n点击登录或切换账号";
        System.Windows.Automation.AutomationProperties.SetName(AccountButton,$"墨墨账号：{accountName??"未登录"}，{status}");
    }

    // Native pixel bounds keep a maximized custom window inside the current
    // monitor's work area, including monitors with negative desktop coordinates.
    private bool SetMaximizedBounds(IntPtr handle, IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return false;
        var monitor = MonitorFromWindow(handle, 2);
        if (monitor == IntPtr.Zero) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var limits = Marshal.PtrToStructure<MinMaxInfo>(pointer);
        var dpi=GetDpiForWindow(handle)/96d;
        if(dpi<=0)dpi=1;
        limits.MinTrackSize=new NativePoint {X=(int)Math.Ceiling(MinWidth*dpi),Y=(int)Math.Ceiling(MinHeight*dpi)};
        limits.MaxPosition = new NativePoint { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
        limits.MaxSize = new NativePoint { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
        Marshal.StructureToPtr(limits, pointer, false);
        return true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
