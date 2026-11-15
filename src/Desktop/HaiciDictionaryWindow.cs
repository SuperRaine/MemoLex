using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MemoLex.Desktop;

internal static class HaiciDictionary
{
    internal static Uri WordUri(string word)
    {
        word=word.Trim();
        if(word.Length is 0 or >100||word.Any(char.IsControl))throw new ArgumentException("请先查询单词。");
        return new("https://dict.cn/"+Uri.EscapeDataString(word));
    }
    internal static bool IsPage(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)
        &&uri.Scheme==Uri.UriSchemeHttps&&uri.IsDefaultPort&&uri.UserInfo.Length==0
        &&(uri.Host.Equals("dict.cn",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".dict.cn",StringComparison.OrdinalIgnoreCase));
    internal static void OpenBrowser(Uri uri)
    {
        if(!IsPage(uri.AbsoluteUri))throw new ArgumentException("无效的海词地址。");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
    }
}

internal sealed class HaiciDictionaryWindow
{
    private readonly WebView2 _browser=new();
    private readonly TextBlock _status=InputDialog.Text("正在打开海词词典…");
    private readonly TextBlock _address=InputDialog.Text("");
    private readonly Button _back,_forward;
    private readonly Action<Uri> _openBrowser;
    private Uri _entry;
    private bool _closed,_initializing;
    internal Window Window {get;}
    internal Uri CurrentUri {get;private set;}
    internal HaiciDictionaryWindow(Window owner,string word,bool initializeBrowser=true,Action<Uri>? openBrowser=null)
    {
        _entry=CurrentUri=HaiciDictionary.WordUri(word);_openBrowser=openBrowser??HaiciDictionary.OpenBrowser;
        Window=InputDialog.Shell(owner,"海词词典",out _);Window.Title="海词词典 · "+word+" · 墨墨搜词";
        Window.Width=Math.Min(1000,SystemParameters.WorkArea.Width-50);Window.Height=Math.Min(800,SystemParameters.WorkArea.Height-50);
        Window.MinWidth=640;Window.MinHeight=460;Window.SizeToContent=SizeToContent.Manual;Window.ResizeMode=ResizeMode.CanResize;
        WindowChrome.GetWindowChrome(Window).ResizeBorderThickness=new Thickness(6);
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var tools=new DockPanel {Margin=new Thickness(14,10,14,10)};
        var browserButton=new Button {Content="用默认浏览器打开",Margin=new Thickness(10,0,0,0)};
        browserButton.Click+=(_,_)=>{try{_openBrowser(CurrentUri);}catch{_status.Text="无法打开默认浏览器，请检查系统的浏览器设置。";}};
        DockPanel.SetDock(browserButton,Dock.Right);tools.Children.Add(browserButton);
        _back=new Button {Content="←",ToolTip="后退",IsEnabled=false,Margin=new Thickness(0,0,6,0)};
        _forward=new Button {Content="→",ToolTip="前进",IsEnabled=false,Margin=new Thickness(0,0,6,0)};
        var reload=new Button {Content="刷新",Margin=new Thickness(0,0,10,0)};
        foreach(var button in new[]{_back,_forward,reload}){DockPanel.SetDock(button,Dock.Left);tools.Children.Add(button);}
        _back.Click+=(_,_)=>{if(_browser.CoreWebView2 is {CanGoBack:true} core)core.GoBack();};
        _forward.Click+=(_,_)=>{if(_browser.CoreWebView2 is {CanGoForward:true} core)core.GoForward();};
        reload.Click+=async(_,_)=>{if(_browser.CoreWebView2!=null)_browser.Reload();else if(initializeBrowser)await InitializeAsync();};
        _address.VerticalAlignment=VerticalAlignment.Center;_address.TextWrapping=TextWrapping.NoWrap;_address.TextTrimming=TextTrimming.CharacterEllipsis;_address.Text=_entry.AbsoluteUri;tools.Children.Add(_address);
        grid.Children.Add(tools);Grid.SetRow(_browser,1);grid.Children.Add(_browser);
        _status.Margin=new Thickness(16,10,16,12);Grid.SetRow(_status,2);grid.Children.Add(_status);InputDialog.SetBody(Window,grid);
        foreach(var (name,element) in new (string,FrameworkElement)[]{("HaiciOpenBrowser",browserButton),("HaiciBack",_back),("HaiciForward",_forward),("HaiciReload",reload),("HaiciAddress",_address),("HaiciStatus",_status)})Window.RegisterName(name,element);
        if(initializeBrowser)Window.Loaded+=async(_,_)=>await InitializeAsync();
        Window.Closed+=(_,_)=>{_closed=true;_browser.Dispose();};
    }
    internal void ShowWord(string word)
    {
        _entry=CurrentUri=HaiciDictionary.WordUri(word);_address.Text=_entry.AbsoluteUri;Window.Title="海词词典 · "+word+" · 墨墨搜词";
        if(_browser.CoreWebView2!=null)_browser.CoreWebView2.Navigate(_entry.AbsoluteUri);
    }
    private async Task InitializeAsync()
    {
        if(_closed||_initializing||_browser.CoreWebView2!=null)return;
        _initializing=true;
        try
        {
            var bundled=Path.Combine(AppContext.BaseDirectory,"webview2");
            var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoLex","dictionary-browser");
            var environment=await CoreWebView2Environment.CreateAsync(File.Exists(Path.Combine(bundled,"msedgewebview2.exe"))?bundled:null,profile);
            if(_closed)return;
            var options=environment.CreateCoreWebView2ControllerOptions();options.ProfileName="Dictionary-"+Guid.NewGuid().ToString("N");options.IsInPrivateModeEnabled=true;
            await _browser.EnsureCoreWebView2Async(environment,options);
            if(_closed)return;
            var core=_browser.CoreWebView2??throw new InvalidOperationException("网页尚未就绪。");
            core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.IsStatusBarEnabled=false;
            core.PermissionRequested+=(_,e)=>{e.State=CoreWebView2PermissionState.Deny;e.Handled=true;};
            core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(HaiciDictionary.IsPage(e.Uri))core.Navigate(e.Uri);};
            core.NavigationStarting+=(_,e)=>
            {
                if(e.Uri=="about:blank")return;
                if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)&&uri.Scheme=="http")
                {
                    var secure=new UriBuilder(uri){Scheme="https",Port=-1}.Uri;
                    if(HaiciDictionary.IsPage(secure.AbsoluteUri)){e.Cancel=true;core.Navigate(secure.AbsoluteUri);return;}
                }
                if(!HaiciDictionary.IsPage(e.Uri)){e.Cancel=true;return;}
                CurrentUri=new(e.Uri);_address.Text=CurrentUri.AbsoluteUri;_status.Text="正在加载海词词典…";
            };
            core.HistoryChanged+=(_,_)=>{_back.IsEnabled=core.CanGoBack;_forward.IsEnabled=core.CanGoForward;};
            core.NavigationCompleted+=(_,e)=>{if(!_closed)_status.Text=e.IsSuccess?"海词词典 · 在线内容":"页面暂时无法加载，可刷新或用默认浏览器打开。";};
            core.Navigate(_entry.AbsoluteUri);
        }
        catch{if(!_closed)_status.Text="暂时无法打开海词词典，可用默认浏览器查看。";}
        finally{_initializing=false;}
    }
}
