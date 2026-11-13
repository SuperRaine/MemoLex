using System.Runtime.InteropServices;

namespace MemoLex.Services;
internal interface IForegroundWindowNative
{
    IntPtr Foreground {get;}
    uint CurrentThread {get;}
    uint WindowThread(IntPtr window);
    void Show(IntPtr window);
    bool Activate(IntPtr window);
    void BringToTop(IntPtr window);
    bool Attach(uint current,uint foreground,bool attach);
    void Focus(IntPtr window);
    void Topmost(IntPtr window,bool value);
}
internal sealed class ForegroundWindow(IForegroundWindowNative? native=null)
{
    private readonly IForegroundWindowNative _native=native??new WindowsForegroundNative();
    internal bool Activate(IntPtr window,bool pinned)
    {
        if(window==IntPtr.Zero)return false;
        _native.Show(window);
        if(_native.Activate(window)&&_native.Foreground==window)return true;
        var current=_native.CurrentThread;var foreground=_native.WindowThread(_native.Foreground);
        var attached=foreground!=0&&foreground!=current&&_native.Attach(current,foreground,true);
        try
        {
            _native.BringToTop(window);_native.Activate(window);_native.Focus(window);
            if(_native.Foreground!=window)
            {
                try{_native.Topmost(window,true);_native.Activate(window);}
                finally{if(!pinned)_native.Topmost(window,false);}
            }
        }
        finally{if(attached)_native.Attach(current,foreground,false);}
        return _native.Foreground==window;
    }
    private sealed class WindowsForegroundNative : IForegroundWindowNative
    {
        public IntPtr Foreground=>GetForegroundWindow();public uint CurrentThread=>GetCurrentThreadId();
        public uint WindowThread(IntPtr window)=>window==IntPtr.Zero?0:GetWindowThreadProcessId(window,out _);
        public void Show(IntPtr window)=>ShowWindow(window,IsIconic(window)?9:5);
        public bool Activate(IntPtr window)=>SetForegroundWindow(window);
        public void BringToTop(IntPtr window)=>BringWindowToTop(window);
        public bool Attach(uint current,uint foreground,bool attach)=>AttachThreadInput(current,foreground,attach);
        public void Focus(IntPtr window){SetActiveWindow(window);SetFocus(window);}
        public void Topmost(IntPtr window,bool value)=>SetWindowPos(window,new IntPtr(value?-1:-2),0,0,0,0,0x43);
        [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")]private static extern bool AttachThreadInput(uint current,uint foreground,bool attach);
        [DllImport("user32.dll")]private static extern IntPtr SetActiveWindow(IntPtr window);
        [DllImport("user32.dll")]private static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    }
}
