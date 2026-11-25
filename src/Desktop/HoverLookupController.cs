using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal sealed class HoverLookupController : IDisposable
{
    private readonly ScreenWordReader _reader=new();
    private readonly HoverLookupWindow _popup;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(90)};
    private readonly HoverMotionGate _motion=new();
    private readonly Func<string,CancellationToken,Task<(DictionaryEntry? Entry,IReadOnlyList<StoredMnemonic> Notes)>> _lookup;
    private readonly Func<bool> _allowed;
    private readonly Window _main;
    private readonly Preferences _preferences;
    private readonly Action<string>? _found;
    private DateTimeOffset _outsideSince,_surfaceChecked;
    private HoverWord? _shown,_dismissed;
    private IntPtr _shownRoot;
    private CancellationTokenSource? _cancel;
    private bool _reading,_disposed,_checkingSurface,_modifiersReady;
    internal bool Enabled {get;set;}
    internal HoverLookupController(Window owner,Preferences preferences,Func<string,CancellationToken,Task<(DictionaryEntry?,IReadOnlyList<StoredMnemonic>)>> lookup,Func<bool> allowed,Action<string> open,
        Func<string,HoverBookmark> bookmarkStatus,Func<string,HoverBookmark,Task<string?>> save,Action<string>? found=null)
    {
        _main=owner;_preferences=preferences;_lookup=lookup;_allowed=allowed;_found=found;
        _popup=new(bookmarkStatus,save);
        _popup.OpenWord+=word=>{SuppressAtCursor();Hide();open(word);};
        _popup.Dismissed+=()=>{_dismissed=_shown;_shown=null;SuppressAtCursor();};
        _timer.Tick+=async(_,_)=>await TickAsync();_timer.Start();
    }
    private void Hide(){_popup.Hide();_shown=null;_outsideSince=default;}
    private void SuppressAtCursor(){_cancel?.Cancel();if(GetPhysicalCursorPos(out var point))_motion.Suppress(new(point.X,point.Y));else _motion.Suppress();}
    private async Task CheckVisibleSurfaceAsync(HoverWord shown,DateTimeOffset now)
    {
        if(_checkingSurface||shown.Surface==null||now-_surfaceChecked<TimeSpan.FromMilliseconds(260))return;
        _checkingSurface=true;_surfaceChecked=now;
        try
        {
            if(!await _reader.IsSurfaceCurrentAsync(shown.Surface)&&ReferenceEquals(shown,_shown)){Hide();SuppressAtCursor();}
        }
        catch{}
        finally{_checkingSurface=false;}
    }
    private async Task TickAsync()
    {
        if(_disposed)return;
        if(!Enabled||!_allowed()||!GetPhysicalCursorPos(out var native))
        {_cancel?.Cancel();Hide();_motion.Reset();_modifiersReady=false;return;}
        var point=new ScreenPoint(native.X,native.Y);var now=DateTimeOffset.UtcNow;
        var moved=_motion.Observe(point,now);
        if(moved){_cancel?.Cancel();if(_dismissed!=null&&!_dismissed.Bounds.Contains(point))_dismissed=null;}
        var modifiers=_preferences.HoverModifiers;
        var ready=((modifiers&1)==0||(GetAsyncKeyState(0x11)&0x8000)!=0)
            &&((modifiers&2)==0||(GetAsyncKeyState(0x12)&0x8000)!=0)&&((modifiers&4)==0||(GetAsyncKeyState(0x10)&0x8000)!=0);
        var insidePopup=_popup.ScreenBounds.Contains(point);
        if(!ready&&!insidePopup){_cancel?.Cancel();Hide();_motion.Suppress(point);_modifiersReady=false;return;}
        if(!_modifiersReady&&modifiers!=0)_motion.Arm(point,now);
        _modifiersReady=true;
        if(((GetAsyncKeyState(1)|GetAsyncKeyState(2))&0x8000)!=0&&!insidePopup){Hide();SuppressAtCursor();return;}
        if(_shown!=null&&!insidePopup&&_shown.Bounds.Contains(point)&&GetAncestor(WindowFromPhysicalPoint(native),2)!=_shownRoot)Hide();
        if(_shown!=null)
        {
            _popup.UpdateBookmark();
            if(insidePopup){_outsideSince=default;_motion.Suppress(point);return;}
            if(_shown.Bounds.Contains(point)){_outsideSince=default;await CheckVisibleSurfaceAsync(_shown,now);return;}
            if(_outsideSince==default)_outsideSince=now;
            if(now-_outsideSince>TimeSpan.FromMilliseconds(160))Hide();else return;
        }
        if(insidePopup||_reading||_dismissed?.Bounds.Contains(point)==true)return;
        var root=GetAncestor(WindowFromPhysicalPoint(native),2);GetWindowThreadProcessId(root,out var pid);
        if(pid==Environment.ProcessId&&root!=new WindowInteropHelper(_main).Handle)return;
        // Failed OCR and changing video frames never re-arm a stationary pointer.
        if(!_motion.TryBegin(now,_preferences.HoverDelayMs))return;
        _reading=true;_cancel?.Dispose();_cancel=new();var cancellation=_cancel.Token;
        try
        {
            var hit=await _reader.ReadAsync(point,cancellation);
            cancellation.ThrowIfCancellationRequested();
            if(hit==null||!GetPhysicalCursorPos(out var current)||!hit.Bounds.Contains(new(current.X,current.Y))||GetAncestor(WindowFromPhysicalPoint(current),2)!=root)return;
            var result=await _lookup(hit.Word,cancellation);cancellation.ThrowIfCancellationRequested();
            if(_disposed||!Enabled||!_allowed()||!GetPhysicalCursorPos(out current)||!hit.Bounds.Contains(new(current.X,current.Y))||GetAncestor(WindowFromPhysicalPoint(current),2)!=root)return;
            if(result.Entry==null||string.IsNullOrWhiteSpace(result.Entry.translation)||hit.Surface==null||!await _reader.IsSurfaceCurrentAsync(hit.Surface,cancellation))return;
            _shown=hit;_shownRoot=root;_surfaceChecked=DateTimeOffset.UtcNow;_popup.ApplyAppearance(_preferences);_popup.Present(hit,result.Entry,result.Notes);
            if(result.Entry.word.Equals(hit.Word,StringComparison.OrdinalIgnoreCase))_found?.Invoke(result.Entry.word);
        }
        catch(OperationCanceledException){}
        catch{}
        finally{_reading=false;}
    }
    public void Dispose(){_disposed=true;_timer.Stop();_cancel?.Cancel();_popup.Close();_reader.Dispose();}
    [StructLayout(LayoutKind.Sequential)]private struct Point {public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetPhysicalCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPhysicalPoint(Point point);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out int processId);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
}
