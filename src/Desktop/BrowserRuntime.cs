using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal sealed record BrowserRuntimeChoice(string Kind,string Version,string? Folder);

internal static class BrowserRuntime
{
    internal const string BootstrapperSha256="cf2f2445fef0c5c14e372045f84998771eeb8bb01d59be4afd00692fa160082e";
    internal const string InstallerName="browser-setup.exe";
    internal const int MinimumMajorVersion=109;
    private static readonly SemaphoreSlim InstallGate=new(1,1);
    internal static string InstallerPath=>Path.Combine(ApplicationPaths.RuntimeDirectory,InstallerName);
    internal static string? SystemVersion=>VersionAt(null);
    private static string? VersionAt(string? folder)
    {
        try
        {
            var version=CoreWebView2Environment.GetAvailableBrowserVersionString(folder);
            // Preview browser channels are not a production runtime dependency.
            return Version.TryParse(version,out var parsed)&&parsed.Major>=MinimumMajorVersion?version:null;
        }
        catch{return null;}
    }
    internal static BrowserRuntimeChoice? Select(string? systemVersion,string? bundledVersion,string bundledFolder)
    {
        if(Version.TryParse(systemVersion,out var system)&&system.Major>=MinimumMajorVersion)return new("system",systemVersion!,null);
        if(Version.TryParse(bundledVersion,out var bundled)&&bundled.Major>=MinimumMajorVersion)return new("bundled",bundledVersion!,bundledFolder);
        return null;
    }
    internal static BrowserRuntimeChoice? Resolve()
    {
        var system=SystemVersion;
        if(system!=null)return new("system",system,null);
        var bundled=Path.Combine(ApplicationPaths.RuntimeDirectory,"webview2");
        return Select(null,File.Exists(Path.Combine(bundled,"msedgewebview2.exe"))?VersionAt(bundled):null,bundled);
    }
    internal static Task<CoreWebView2Environment> CreateEnvironmentAsync(string profileDirectory)
    {
        var runtime=Resolve()??throw new InvalidOperationException("请先安装网页组件，再重试。");
        return CoreWebView2Environment.CreateAsync(runtime.Folder,profileDirectory);
    }
    internal static bool VerifyInstaller(string path)
    {
        if(!File.Exists(path))return false;
        using var input=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).Equals(BootstrapperSha256,StringComparison.OrdinalIgnoreCase);
    }
    internal static async Task InstallAsync(CancellationToken cancellation)
    {
        await InstallGate.WaitAsync(cancellation);
        try
        {
            if(SystemVersion!=null)return;
            if(!VerifyInstaller(InstallerPath))throw new InvalidOperationException("网页组件安装文件缺失或不完整，请重新解压完整软件包。");
            cancellation.ThrowIfCancellationRequested();
            // This runs only after the user clicks Install in a web window.
            // No elevation is requested; Microsoft handles per-user deployment.
            using var process=Process.Start(new ProcessStartInfo(InstallerPath,"/silent /install")
                {UseShellExecute=false,CreateNoWindow=true})??throw new IOException("无法启动网页组件安装程序。");
            await process.WaitForExitAsync(cancellation).WaitAsync(TimeSpan.FromMinutes(5),cancellation);
            if(process.ExitCode==0)
                for(var attempt=0;attempt<20&&SystemVersion==null;attempt++)await Task.Delay(500,cancellation);
            if(process.ExitCode!=0||SystemVersion==null)throw new IOException("网页组件未能安装完成，请检查网络后重试。");
        }
        finally{InstallGate.Release();}
    }
}
