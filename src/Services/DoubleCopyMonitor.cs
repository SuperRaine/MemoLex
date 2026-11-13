using System.Runtime.InteropServices;
using MemoLex.Core;

namespace MemoLex.Services;

// Passes all keystrokes through unchanged. Content is read by the UI only after a double copy.
internal sealed class DoubleCopyMonitor : IDisposable
{
    private readonly DoubleCopyDetector _detector=new();
    private readonly HookProc _callback;
    private readonly Action<DoubleCopySignal> _copied;
    private IntPtr _hook;
    public DoubleCopyMonitor(Action<DoubleCopySignal> copied)
    {
        _copied=copied;_callback=Handle;
        _hook=SetWindowsHookEx(13,_callback,GetModuleHandle(null),0);
        if(_hook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr Handle(int code,IntPtr message,IntPtr pointer)
    {
        if(code>=0)
        {
            var key=Marshal.PtrToStructure<KeyboardData>(pointer);
            var down=message.ToInt32() is 0x100 or 0x104;
            // C is the current event; modifier states refer to earlier key events.
            var signal=_detector.Key((int)key.Key,down,(GetAsyncKeyState(0x11)&0x8000)!=0,
                (GetAsyncKeyState(0x12)&0x8000)!=0||(GetAsyncKeyState(0x5B)&0x8000)!=0||(GetAsyncKeyState(0x5C)&0x8000)!=0||(GetAsyncKeyState(0x10)&0x8000)!=0,
                (key.Flags&0x10)!=0,Environment.TickCount64,GetClipboardSequenceNumber(),GetForegroundWindow());
            if(signal!=null)_copied(signal);
        }
        return CallNextHookEx(_hook,code,message,pointer);
    }
    public void Dispose(){if(_hook!=IntPtr.Zero){UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential)]private struct KeyboardData{public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
    private delegate IntPtr HookProc(int code,IntPtr message,IntPtr pointer);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetWindowsHookEx(int hook,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr pointer);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
}
