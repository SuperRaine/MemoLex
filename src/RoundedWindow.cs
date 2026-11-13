using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace MemoLex;

internal static class RoundedWindow
{
    internal const double Radius=12;
    internal static void Attach(Window window)
    {
        void Update()
        {
            if(WindowChrome.GetWindowChrome(window) is { } chrome)
                chrome.CornerRadius=new CornerRadius(window.WindowState==WindowState.Maximized?0:Radius);
            var handle=new WindowInteropHelper(window).Handle;
            if(handle==IntPtr.Zero)return;
            var preference=window.WindowState==WindowState.Maximized?1:2;
            // WindowChrome also supplies a rounded native region on systems
            // that do not implement the Windows 11 corner preference.
            DwmSetWindowAttribute(handle,33,ref preference,sizeof(int));
        }
        Update();
        window.SourceInitialized+=(_,_)=>Update();
        window.StateChanged+=(_,_)=>Update();
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
