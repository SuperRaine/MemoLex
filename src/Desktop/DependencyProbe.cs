using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal static class DependencyProbe
{
    internal static async Task<Dictionary<string,object?>> RunAsync()
    {
        var result=new Dictionary<string,object?>();
        byte[] Raster(string text,string font)
        {
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.White,null,new Rect(0,0,600,100));
                draw.DrawText(new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(font),44,Brushes.Black,1),new Point(24,16));
            }
            var target=new RenderTargetBitmap(600,100,96,96,PixelFormats.Pbgra32);target.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));using var bytes=new MemoryStream();encoder.Save(bytes);return bytes.ToArray();
        }
        using(var reader=new ScreenWordReader())
        {
            result["englishImageOcr"]=reader.Recognize(Raster("insight","Arial"),new(70,46),new(0,0),1)?.Word=="insight";
        }
        var runtime=Path.Combine(ApplicationPaths.RuntimeDirectory,"webview2");
        result["bundledBrowserExists"]=File.Exists(Path.Combine(runtime,"msedgewebview2.exe"));
        var choice=BrowserRuntime.Resolve();
        result["browserRuntimeSource"]=choice?.Kind??"missing";
        result["browserInstallerVerified"]=BrowserRuntime.VerifyInstaller(BrowserRuntime.InstallerPath);
        var profile=Path.Combine(Path.GetTempPath(),"MemoLex.BrowserProbe."+Guid.NewGuid().ToString("N"));
        using var browser=new WebView2();var window=new Window{Content=browser,Width=640,Height=480,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();
            var environment=await BrowserRuntime.CreateEnvironmentAsync(profile);
            await browser.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(20));
            result["browserVersion"]=environment.BrowserVersionString;
            var complete=new TaskCompletionSource<bool>();browser.CoreWebView2.NavigationCompleted+=(_,args)=>complete.TrySetResult(args.IsSuccess);
            browser.NavigateToString("<!doctype html><meta charset=utf-8><p>MemoLex local dependency check</p>");
            result["browserRender"]=await complete.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally{window.Close();}
        result["ok"]=result.GetValueOrDefault("englishImageOcr") is true&&result.GetValueOrDefault("browserRender") is true;
        return result;
    }
}
