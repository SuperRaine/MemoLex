using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal sealed class HoverLookupWindow : Window
{
    private readonly TextBlock _word=new(){FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,MaxHeight=65};
    private readonly TextBlock _pronunciation=InputDialog.Text("");
    private readonly TextBlock _meaning=InputDialog.Text("");
    private readonly TextBlock _mnemonic=InputDialog.Text("");
    internal event Action<string>? OpenWord;
    internal event Action? Dismissed;
    private string _currentWord="";
    internal HoverLookupWindow()
    {
        Width=370;MaxHeight=380;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        var content=new StackPanel {Margin=new Thickness(17,13,17,14)};
        var title=new DockPanel();var close=new Button {Content="×",Padding=new Thickness(7,1,7,1),Background=Brushes.Transparent,BorderThickness=new Thickness(0)};DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(_word);content.Children.Add(title);
        close.Click+=(_,_)=>{Hide();Dismissed?.Invoke();};
        _pronunciation.Margin=new Thickness(0,3,0,10);content.Children.Add(_pronunciation);
        var definitions=new StackPanel();_meaning.FontSize=14;_meaning.LineHeight=22;definitions.Children.Add(_meaning);
        _mnemonic.Margin=new Thickness(0,12,0,0);_mnemonic.FontSize=12;definitions.Children.Add(_mnemonic);
        content.Children.Add(new ScrollViewer{Content=definitions,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=205});
        var open=new Button {Content="查看完整释义 →",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,13,0,0),Padding=new Thickness(9,5,9,5)};
        open.Click+=(_,_)=>OpenWord?.Invoke(_currentWord);content.Children.Add(open);
        Content=new Border {Child=content,CornerRadius=new CornerRadius(10),Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(113,205,249)),BorderThickness=new Thickness(1),Padding=new Thickness(1)};
        SourceInitialized+=(_,_)=>
        {
            var hwnd=new WindowInteropHelper(this).Handle;var style=GetWindowLongPtr(hwnd,-20);
            SetWindowLongPtr(hwnd,-20,new IntPtr(style.ToInt64()|0x08000000|0x00000080));
        };
    }
    internal void Present(HoverWord hit,DictionaryEntry entry,IReadOnlyList<StoredMnemonic> notes)
    {
        SetContent(hit,entry,notes);
        var opacity=Opacity;Opacity=0;
        if(!IsVisible){Left=-32000;Top=-32000;Show();}UpdateLayout();
        var hwnd=new WindowInteropHelper(this).Handle;var dpi=GetDpiForWindow(hwnd)/96d;
        var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)hit.Bounds.X,(int)hit.Bounds.Y)).WorkingArea;
        var width=(int)Math.Ceiling(ActualWidth*dpi);var height=(int)Math.Ceiling(ActualHeight*dpi);
        var x=Math.Clamp((int)hit.Bounds.X,screen.Left,Math.Max(screen.Left,screen.Right-width));
        var y=(int)(hit.Bounds.Y+hit.Bounds.Height+7);if(y+height>screen.Bottom)y=Math.Max(screen.Top,(int)hit.Bounds.Y-height-7);
        SetWindowPos(hwnd,new IntPtr(-1),x,y,0,0,0x0010|0x0001);Opacity=opacity;
    }
    internal void SetContent(HoverWord hit,DictionaryEntry entry,IReadOnlyList<StoredMnemonic> notes)
    {
        _currentWord=entry.word;_word.Text=entry.word;
        _pronunciation.Text=entry.word.Equals(hit.Word,StringComparison.OrdinalIgnoreCase)?(entry.phonetic.Length>0?entry.phonetic+" · ":"")+"离线释义 · "+hit.Method:"未找到 “"+hit.Word+"”，相近单词："+entry.word+" · 离线释义";
        _meaning.Text=entry.translation.Length<=600?entry.translation:entry.translation[..600]+"…";
        _mnemonic.Text=notes.FirstOrDefault()?.Text??"";if(_mnemonic.Text.Length>280)_mnemonic.Text=_mnemonic.Text[..280]+"…";
        _mnemonic.Visibility=_mnemonic.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
    internal void ApplyAppearance(Preferences preferences)
    {
        Opacity=preferences.HoverTranslucent?0.88:1;
        var scale=preferences.HoverFontSize switch{"small"=>0.86,"large"=>1.18,_=>1d};
        _word.FontSize=23*scale;_meaning.FontSize=14*scale;_meaning.LineHeight=22*scale;_mnemonic.FontSize=12*scale;
    }
    internal ScreenBox ScreenBounds
    {
        get{if(!IsVisible||!GetWindowRect(new WindowInteropHelper(this).Handle,out var r))return default;return new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr handle,int index,IntPtr value);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr handle,out Rect rectangle);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr handle);
}
