using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal sealed class OfficialLoginWindow
{
    private readonly Window _window;
    private readonly WebView2 _browser=new();
    private readonly TextBlock _status=InputDialog.Text("正在打开墨墨官方登录页…");
    private readonly TextBlock _address=InputDialog.Text("https://accounts.maimemo.com");
    private readonly Button _installBrowser=new(){Content="安装网页组件",Visibility=Visibility.Collapsed,Margin=new Thickness(10,0,0,0)};
    private readonly TokenStore _store;
    private readonly SavedAccount? _target;
    private readonly CancellationTokenSource _cancel=new();
    private readonly string _profileDirectory;
    private readonly Func<OfficialAuthorization,CancellationToken,Task>? _validate;
    private readonly Func<BrowserRuntimeChoice?> _resolveRuntime;
    private bool _completing,_closed;
    private string _identity="";
    internal SavedAccount? Result {get;private set;}
    internal Window Window=>_window;
    internal OfficialLoginWindow(Window owner,TokenStore store,SavedAccount? target=null,string? profileDirectory=null,Func<OfficialAuthorization,CancellationToken,Task>? validate=null,Func<BrowserRuntimeChoice?>? runtimeResolver=null)
    {
        _store=store;_target=target;_validate=validate;_identity=target?.Identity??"";_resolveRuntime=runtimeResolver??BrowserRuntime.Resolve;
        _profileDirectory=profileDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoLex","official-login");
        _window=InputDialog.Shell(owner,"登录墨墨官方账号",out _);
        _window.Width=Math.Min(760,SystemParameters.WorkArea.Width-50);_window.Height=Math.Min(840,SystemParameters.WorkArea.Height-45);
        _window.MinWidth=500;_window.MinHeight=550;_window.SizeToContent=SizeToContent.Manual;_window.ResizeMode=ResizeMode.CanResize;
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        _address.Margin=new Thickness(18,10,18,10);grid.Children.Add(_address);Grid.SetRow(_browser,1);grid.Children.Add(_browser);
        var footer=new DockPanel {Margin=new Thickness(18,10,18,14)};
        var cancel=new Button {Content="取消",IsCancel=true,Margin=new Thickness(10,0,0,0)};DockPanel.SetDock(cancel,Dock.Right);footer.Children.Add(cancel);
        var refresh=new Button {Content="重新打开",Margin=new Thickness(10,0,0,0)};refresh.Click+=(_,_)=>Navigate();DockPanel.SetDock(refresh,Dock.Right);footer.Children.Add(refresh);
        _installBrowser.Click+=async(_,_)=>await InstallBrowserAsync();DockPanel.SetDock(_installBrowser,Dock.Right);footer.Children.Add(_installBrowser);
        _status.TextWrapping=TextWrapping.Wrap;_status.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(_status);Grid.SetRow(footer,2);grid.Children.Add(footer);
        InputDialog.SetBody(_window,grid);
        _window.RegisterName("OfficialLoginBrowser",_browser);_window.RegisterName("LoginInstallBrowser",_installBrowser);_window.RegisterName("OfficialLoginStatus",_status);
        _window.Loaded+=async(_,_)=>await InitializeAsync();
        _window.Closed+=(_,_)=>{_closed=true;_cancel.Cancel();_browser.Dispose();};
    }
    internal bool? ShowDialog()=>_window.ShowDialog();
    private void Navigate()
    {
        if(_browser.CoreWebView2==null){_=InitializeAsync();return;}
        _status.Text="请在墨墨官方页面使用密码或验证码登录，完成后会自动返回软件。";
        _browser.CoreWebView2.Navigate(OfficialLoginProtocol.StartUrl);
    }
    private async Task InitializeAsync()
    {
        try
        {
            if(_browser.CoreWebView2!=null){Navigate();return;}
            if(_resolveRuntime()==null)
            {
                _installBrowser.Visibility=Visibility.Visible;
                _status.Text="首次登录需要安装网页组件。请联网安装，完成后会自动继续。";
                return;
            }
            _installBrowser.Visibility=Visibility.Collapsed;
            var environment=await BrowserRuntime.CreateEnvironmentAsync(_profileDirectory);
            if(_closed)return;
            var options=environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName="MemoLex-"+Guid.NewGuid().ToString("N");options.IsInPrivateModeEnabled=true;
            await _browser.EnsureCoreWebView2Async(environment,options);
            if(_closed)return;
            var core=_browser.CoreWebView2??throw new InvalidOperationException("登录组件未就绪。");
            core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsStatusBarEnabled=false;
            core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
            core.PermissionRequested+=(_,e)=>{e.State=CoreWebView2PermissionState.Deny;e.Handled=true;};
            core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(OfficialLoginProtocol.AllowedNavigation(e.Uri))core.Navigate(e.Uri);};
            core.NavigationStarting+=(_,e)=>
            {
                if(e.Uri=="about:blank")return;
                if(!OfficialLoginProtocol.AllowedNavigation(e.Uri)){e.Cancel=true;_status.Text="此链接不属于墨墨官方登录页面。";return;}
                if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri))_address.Text="墨墨官方登录 · https://"+uri.Host;
            };
            core.WebMessageReceived+=(_,e)=>
            {
                if(!OfficialLoginProtocol.IsIdentitySource(e.Source))return;
                try
                {
                    using var json=JsonDocument.Parse(e.WebMessageAsJson);var value=json.RootElement;
                    if(value.TryGetProperty("kind",out var kind)&&kind.GetString()=="memolex-identity"&&value.TryGetProperty("identity",out var id)&&id.ValueKind==JsonValueKind.String)
                    {var identity=id.GetString()!.Trim();if(identity.Length is >0 and <=200)_identity=identity;}
                }
                catch(JsonException){}
            };
            var identityJson=JsonSerializer.Serialize(_identity);
            await core.AddScriptToExecuteOnDocumentCreatedAsync("""
                (()=>{
                  if(location.origin!=='https://accounts.maimemo.com')return;
                  document.addEventListener('submit',e=>{
                    const input=e.target.querySelector('input[name="identity"]');
                    if(input)window.chrome.webview.postMessage({kind:'memolex-identity',identity:input.value});
                  },true);
                })();
                """);
            core.NavigationCompleted+=async(_,e)=>
            {
                if(_closed)return;
                if(!e.IsSuccess){_status.Text="暂时无法打开墨墨登录页，请检查网络后点击「重新打开」。";return;}
                try
                {
                    if(OfficialLoginProtocol.IsIdentitySource(core.Source)&&_identity.Length>0)
                        await core.ExecuteScriptAsync("document.querySelectorAll('input[name=identity]').forEach(x=>{x.value="+identityJson+";x.dispatchEvent(new Event('input',{bubbles:true}));});");
                    if(OfficialLoginProtocol.IsTokenDocument(core.Source))
                    {
                        _status.Text="正在完成登录，请稍候…";
                        var body=await core.ExecuteScriptAsync("document.documentElement.outerHTML");
                        var text=JsonSerializer.Deserialize<string>(body)??"";
                        if(OfficialLoginProtocol.Parse(core.Source,text,DateTimeOffset.UtcNow) is {} auth)await CompleteAsync(auth);
                        else if(!_completing)_status.Text="暂时无法完成登录，请点击「重新打开」后重试。";
                    }
                }
                catch(Exception){if(!_closed)_status.Text="暂时无法完成登录，请点击「重新打开」后重试。";}
            };
            core.WebResourceResponseReceived+=async(_,e)=>
            {
                if(_closed||!OfficialLoginProtocol.IsTokenDocument(e.Request.Uri)||e.Response.StatusCode!=200)return;
                try
                {
                    var type=e.Response.Headers.Contains("Content-Type")?e.Response.Headers.GetHeader("Content-Type"):"";
                    if(!type.Contains("json",StringComparison.OrdinalIgnoreCase)&&!type.Contains("text/plain",StringComparison.OrdinalIgnoreCase)&&!type.Contains("text/html",StringComparison.OrdinalIgnoreCase))return;
                    using var content=await e.Response.GetContentAsync();
                    if(content==null)return;
                    using var memory=new MemoryStream();var buffer=new byte[4096];
                    while(true){var count=await content.ReadAsync(buffer,_cancel.Token);if(count==0)break;if(memory.Length+count>65536)return;memory.Write(buffer,0,count);}
                    if(OfficialLoginProtocol.Parse(e.Request.Uri,Encoding.UTF8.GetString(memory.ToArray()),DateTimeOffset.UtcNow) is {} auth)await CompleteAsync(auth);
                }
                catch(Exception){if(!_closed)_status.Text="未能获取登录信息，请点击「重新打开」后重试。";}
            };
            Navigate();
        }
        catch(Exception){if(!_closed)_status.Text="登录页面未能启动，请点击「重新打开」后重试。";}
    }
    private async Task InstallBrowserAsync()
    {
        _installBrowser.IsEnabled=false;_status.Text="正在安装网页组件，请稍候；完成后会自动继续登录。";
        try{await BrowserRuntime.InstallAsync(_cancel.Token);if(!_closed)await InitializeAsync();}
        catch(OperationCanceledException){}
        catch(Exception error){if(!_closed)_status.Text=error is InvalidOperationException or IOException?error.Message:"网页组件安装未完成，请检查网络后重试。";}
        finally{if(!_closed)_installBrowser.IsEnabled=true;}
    }
    private async Task CompleteAsync(OfficialAuthorization authorization)
    {
        if(_closed||_completing)return;_completing=true;_status.Text="正在确认登录状态…";
        try
        {
            if(_validate!=null)await _validate(authorization,_cancel.Token);
            else
            {
                using var api=new MaiMemoClient(TokenStore.InMemory(authorization.Token));
                if(await api.ResolveWordAsync("hello",_cancel.Token)==null)throw new MaiMemoException("暂未能确认官方授权。");
            }
            _cancel.Token.ThrowIfCancellationRequested();
            Result=_store.SaveAccountToken(authorization.Token,_identity,null,authorization.ExpiresAt,_target?.Id);
            _window.DialogResult=true;
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!_closed){_completing=false;_status.Text=error is ArgumentException or InvalidOperationException?error.Message:"未能完成登录，请检查网络，并确认已在墨墨页面同意授权，然后点击「重新打开」。";}}
    }
}
