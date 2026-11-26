using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private bool? _compactLayout;
    private bool? _narrowLayout;
    private bool? _shortLayout;
    private void UpdateResponsiveLayout(Size size)
    {
        var shortWindow = size.Height < 620;
        if (_shortLayout != shortWindow)
        {
            _shortLayout = shortWindow;
            SidebarLayout.Margin = new Thickness(19, shortWindow ? 8 : 11, 19, shortWindow ? 8 : 11);
            SearchHeadingRow.Margin = new Thickness(1, 0, 1, shortWindow ? 6 : 12);
            CloudNotepadCard.Margin = new Thickness(0, shortWindow ? 6 : 12, 0, 0);
        }

        var compact=size.Width<960;
        var narrow=size.Width<900;
        if(_compactLayout!=compact||_narrowLayout!=narrow)
        {
            _compactLayout=compact;
            _narrowLayout=narrow;
            SidebarColumn.Width=new GridLength(compact?224:268);
            BrandColumn.Width=GridLength.Auto;
            BrandTitlePanel.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
            BrandLogo.Margin=new Thickness(0,0,narrow?0:(compact?7:9),0);
            BrandGroup.Margin=new Thickness(0,0,narrow?6:(compact?8:12),0);
            BrandLogo.Width=BrandLogo.Height=compact?28:34;BrandTitleText.FontSize=compact?14.5:16.5;
            NavSeparator.Margin=new Thickness(0,0,narrow?6:(compact?8:12),0);
            BackButton.Width=ForwardButton.Width=narrow?26:(compact?28:32);
            MinimizeWindowButton.Width=MaximizeButton.Width=CloseWindowButton.Width=narrow?30:(compact?32:40);
            var iconWidth=narrow?26:(compact?28:34);
            var iconMargin=new Thickness(0,0,narrow?0:(compact?1:2),0);
            QuickLookupButton.Width=DashboardButton.Width=ThemeToggleButton.Width=SettingsButton.Width=PinButton.Width=iconWidth;
            QuickLookupButton.Margin=DashboardButton.Margin=ThemeToggleButton.Margin=iconMargin;
            SettingsButton.Margin=new Thickness(0,0,narrow?0:(compact?1:3),0);
            PinButton.Margin=new Thickness(0,0,narrow?2:(compact?3:6),0);
            WindowControlsSeparator.Margin=new Thickness(0,0,narrow?2:(compact?3:6),0);
            AccountButton.Margin=new Thickness(0,0,narrow?2:(compact?3:5),0);
            ExamCountdownBadge.Margin=new Thickness(0,0,narrow?4:(compact?6:10),0);
            ExamCountdownBadge.Padding=narrow?new Thickness(7,3,7,3):compact?new Thickness(8,4,8,4):new Thickness(10,4,10,4);
            HeaderCenterGrid.Margin=narrow?new Thickness(6,0,6,0):compact?new Thickness(6,0,6,0):new Thickness(10,0,10,0);
            CloudStatusText.MaxWidth=narrow?56:(compact?70:104);
            AccountButton.MaxWidth=narrow?88:(compact?112:152);
            AccountButton.Padding=narrow?new Thickness(7,5,7,5):compact?new Thickness(8,6,8,6):new Thickness(11,6,11,6);
            ExamCountdownText.MaxWidth=narrow?84:(compact?115:200);
            UpdateExamCountdown();
            MainContentGrid.Margin=compact?new Thickness(15,15,15,13):new Thickness(26,20,26,16);
            WordCard.Padding=compact?new Thickness(18):new Thickness(28,24,28,26);
            WordText.FontSize=compact?32:40;
            WordActions.Orientation=compact?System.Windows.Controls.Orientation.Vertical:System.Windows.Controls.Orientation.Horizontal;
            AddTodayButton.Margin=compact?new Thickness(0,6,0,0):new Thickness(8,0,0,0);
            AddTodayButton.Padding=compact?new Thickness(10,7,10,7):new Thickness(14,10,14,10);
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
        CloudStatusText.Foreground = Brush(status == "已连接" ? "#0A7FB3" : status is "登录已失效" or "登录失效" or "连接受限" ? "#B45309" : "#648198");
        AccountStatusDot.Fill = Brush(status == "已连接" ? "#17B877" : status is "登录已失效" or "登录失效" or "连接受限" ? "#F59E0B" : "#94A3B8");
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
