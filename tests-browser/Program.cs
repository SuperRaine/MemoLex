using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using MemoLex.Desktop;
using MemoLex.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/MemoLex;component/Theme.xaml",UriKind.Relative)});
        var root=Path.Combine(Path.GetTempPath(),"MemoLex.BrowserTests."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=0;var exitCode=1;
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        void Pass(string text){passed++;Console.WriteLine("PASS "+text);}
        async Task Wait(Func<bool> condition,string message)
        {
            var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed>TimeSpan.FromSeconds(25))throw new TimeoutException(message);await Task.Delay(50);}
        }
        void Show(Window window)
        {
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
        }
        app.Dispatcher.InvokeAsync(async()=>
        {
            try
            {
                var system=BrowserRuntime.Select("157.0.1.1","158.0.1.1","optional-browser");
                Check(system is {Kind:"system",Folder:null,Version:"157.0.1.1"},"An app-private runtime overrode the shared runtime");
                Pass("The installed shared runtime wins even when an optional private copy is newer");
                Check(BrowserRuntime.Select(null,"157.0.1.1","optional-browser") is {Kind:"bundled",Folder:"optional-browser"},"A legacy optional runtime no longer works");
                Pass("Existing full installations retain their optional runtime fallback");
                Check(BrowserRuntime.Select(null,null,"optional-browser")==null&&BrowserRuntime.Select("157.0.1.1 dev",null,"optional-browser")==null&&BrowserRuntime.Select("0.0.0.0",null,"optional-browser")==null
                    &&BrowserRuntime.Select("108.0.0.0",null,"optional-browser")==null&&BrowserRuntime.Select("109.0.0.0",null,"optional-browser")?.Kind=="system","An outdated or preview browser became a production dependency");
                Pass("Missing and unsupported preview runtimes require explicit installation");

                var original=args.FirstOrDefault()??Path.Combine(AppContext.BaseDirectory,BrowserRuntime.InstallerName);
                Check(BrowserRuntime.VerifyInstaller(original),"The official bootstrapper differs from the verified release bytes");
                var changed=Path.Combine(root,"changed.exe");var bytes=File.ReadAllBytes(original);bytes[^1]^=1;File.WriteAllBytes(changed,bytes);
                Check(!BrowserRuntime.VerifyInstaller(changed)&&!BrowserRuntime.VerifyInstaller(Path.Combine(root,"absent.exe")),"A changed or missing installer was accepted");
                Pass("The small Microsoft installer is pinned and changed executables are rejected");

                var store=new TokenStore(Path.Combine(root,"unused-token.dpapi"));
                var login=new OfficialLoginWindow(null!,store,profileDirectory:Path.Combine(root,"missing-login"),runtimeResolver:()=>null);
                try
                {
                    Show(login.Window);await Wait(()=>((Button)login.Window.FindName("LoginInstallBrowser")).IsVisible,"Missing-runtime login prompt");
                    Check(((TextBlock)login.Window.FindName("OfficialLoginStatus")).Text.Contains("安装网页组件")||((TextBlock)login.Window.FindName("OfficialLoginStatus")).Text.Contains("安装网页"),"Installation instructions were missing");
                    Check(!store.HasToken,"Showing an installation prompt created an account credential");
                    Pass("A fresh login without a web runtime shows an install action without writing an account");
                }
                finally{login.Window.Close();}
                Uri? opened=null;var haici=new HaiciDictionaryWindow(null!,"talkative",openBrowser:uri=>opened=uri,runtimeResolver:()=>null);
                try
                {
                    Show(haici.Window);await Wait(()=>((Button)haici.Window.FindName("HaiciInstallBrowser")).IsVisible,"Missing-runtime dictionary prompt");
                    ((Button)haici.Window.FindName("HaiciOpenBrowser")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(opened?.AbsoluteUri=="https://dict.cn/talkative","The default-browser action was blocked by a missing embedded runtime");
                    Pass("Dictionary pages offer installation and keep the default-browser fallback");
                }
                finally{haici.Window.Close();}

                if(args.Contains("--live"))
                {
                    Check(BrowserRuntime.Resolve() is {Kind:"system"},"This live check requires an installed shared runtime");
                    var before=BrowserRuntime.SystemVersion;await BrowserRuntime.InstallAsync(CancellationToken.None);
                    Check(BrowserRuntime.SystemVersion==before,"An existing runtime was replaced instead of reused");
                    Pass("The existing runtime is reused without launching a component installation");
                    using var browser=new WebView2();var window=new Window{Content=browser,Width=640,Height=480};
                    try
                    {
                        Show(window);var environment=await BrowserRuntime.CreateEnvironmentAsync(Path.Combine(root,"render"));
                        await browser.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(20));
                        var complete=new TaskCompletionSource<bool>();browser.CoreWebView2.NavigationCompleted+=(_,e)=>complete.TrySetResult(e.IsSuccess);
                        browser.NavigateToString("<!doctype html><meta charset=utf-8><title>MemoLex shared component check</title><p>ready</p>");
                        Check(await complete.Task.WaitAsync(TimeSpan.FromSeconds(15)),"The installed runtime could not render content");
                        Console.WriteLine("Shared runtime version: "+environment.BrowserVersionString);
                        Pass("Real web content renders using the shared runtime with no private browser folder");
                    }
                    finally{window.Close();}

                    var official=new OfficialLoginWindow(null!,new TokenStore(Path.Combine(root,"official-unused.dpapi")),profileDirectory:Path.Combine(root,"official-page"));
                    try
                    {
                        var browserControl=(WebView2)official.Window.FindName("OfficialLoginBrowser");
                        Show(official.Window);
                        await Wait(()=>browserControl.CoreWebView2!=null&&OfficialLoginProtocol.IsIdentitySource(browserControl.CoreWebView2.Source),"Official login document");
                        var loaded=false;browserControl.CoreWebView2!.NavigationCompleted+=(_,e)=>{if(e.IsSuccess)loaded=true;};
                        // Navigation may complete between observing its URL and subscribing.
                        var readyDeadline=Stopwatch.StartNew();
                        while(!loaded)
                        {
                            if((await browserControl.CoreWebView2.ExecuteScriptAsync("document.readyState")).Contains("complete"))break;
                            if(readyDeadline.Elapsed>TimeSpan.FromSeconds(25))throw new TimeoutException("Official page readiness");
                            await Task.Delay(50);
                        }
                        Check(!((Button)official.Window.FindName("LoginInstallBrowser")).IsVisible,"A working shared runtime still requested installation");
                        Pass("The official login page opens in an isolated profile without entering credentials");
                    }
                    finally{official.Window.Close();}
                }
                Console.WriteLine($"{passed} browser-runtime checks passed; no credentials were submitted and no installer was executed.");
                exitCode=0;
            }
            catch(Exception error){Console.Error.WriteLine(error);}
            finally{app.Shutdown();}
        });
        app.Run();return exitCode;
    }
}
