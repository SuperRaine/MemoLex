using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal sealed class UpdateManager:IDisposable
{
    private readonly Preferences _preferences;
    private readonly GitHubUpdateClient _client;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMinutes(1)};
    private readonly Action _exit;
    private readonly Version _current;
    private CancellationTokenSource? _operation;
    private bool _busy,_automatic,_disposed;
    private UpdateRelease? _release;
    private UpdateAsset? _asset;
    private string? _download;
    internal string Status {get;private set;}="点击「手动检查更新」获取最新版本。";
    internal int Progress {get;private set;}
    internal event Action? Changed;
    internal bool Busy=>_busy;
    internal bool CanDownload=>!_busy&&_asset!=null&&_download==null;
    internal bool CanInstall=>!_busy&&_asset!=null&&_download!=null;
    internal UpdateRelease? Release=>_release;
    internal UpdateManager(Preferences preferences,Action exit,GitHubUpdateClient? client=null,Version? current=null)
    {
        _preferences=preferences;_exit=exit;_client=client??new();_current=current??Version.Parse(SettingsDialog.Version);
        if(GitHubUpdateClient.TryVersion(_preferences.LastPublicVersion,out var previous)&&_preferences.LastSuccessfulUpdateCheck!=null)Status=$"上次检查到的正式版为 {previous}。可手动检查是否有更新。";
        _timer.Tick+=async(_,_)=>await CheckScheduledAsync(false);
    }
    internal void Start(){_timer.Start();_=CheckScheduledAsync(true);}
    private async Task CheckScheduledAsync(bool startup)
    {
        if(!_busy&&GitHubUpdateClient.CheckDue(_preferences.AutomaticUpdates,_preferences.UpdateFrequency,_preferences.LastUpdateCheck,DateTimeOffset.UtcNow,startup))await CheckAsync(true);
    }
    internal void PolicyChanged()
    {
        _preferences.Save();if(!_preferences.AutomaticUpdates&&_automatic)_operation?.Cancel();Changed?.Invoke();
    }
    private void Notify(string text){Status=text;Changed?.Invoke();}
    internal void Cancel()=>_operation?.Cancel();
    internal async Task CheckAsync(bool automatic=false)
    {
        if(_busy||_disposed)return;_busy=true;_automatic=automatic;_operation?.Dispose();_operation=new();var token=_operation.Token;
        _preferences.LastUpdateCheck=DateTimeOffset.UtcNow;_preferences.Save();Progress=0;
        Notify(GitHubUpdateClient.TryVersion(_preferences.LastPublicVersion,out var previous)?$"正在检查更新…（上次检查到 {previous}）":"正在检查更新…");
        try
        {
            var release=await _client.GetLatestAsync(_current,token);
            _preferences.LastPublicVersion=release.Version.ToString();_preferences.LastSuccessfulUpdateCheck=DateTimeOffset.UtcNow;_preferences.Save();
            if(release.Version<=_current){_release=null;_asset=null;_download=null;Notify(release.Version<_current?$"当前版本 {_current} 比已发布的正式版 {release.Version} 更新，无需升级。":$"当前版本 {_current} 已是最新正式版。");return;}
            if(_release?.Version!=release.Version)_download=null;
            _release=release;_asset=IsMsiInstallation()?release.Installer:release.Portable;
            if(_asset==null){Notify($"发现新版 {release.Version}，暂不支持直接更新。请前往下载页面手动安装。");return;}
            Notify($"发现新版 {release.Version}。"+(automatic?"正在自动下载…":"可下载更新。"));
            if(automatic&&_preferences.AutomaticUpdates)await DownloadCoreAsync(token);
        }
        catch(OperationCanceledException){Notify(token.IsCancellationRequested?"已取消检查或下载更新。":"更新检查超时，请稍后重试。");}
        catch(Exception error){Notify(error is UpdateException?error.Message:"暂时无法检查更新，请检查网络后重试。");}
        finally{_busy=false;_automatic=false;Changed?.Invoke();}
    }
    internal async Task DownloadAsync()
    {
        if(!CanDownload||_disposed)return;_busy=true;_automatic=false;_operation?.Dispose();_operation=new();
        try{await DownloadCoreAsync(_operation.Token);}
        catch(OperationCanceledException){Notify("下载已取消，可随时重新下载。");}
        catch(Exception error){Notify(error is UpdateException?error.Message:"下载失败，请检查网络或磁盘空间后重试。");}
        finally{_busy=false;Changed?.Invoke();}
    }
    private async Task DownloadCoreAsync(CancellationToken token)
    {
        if(_asset==null||_release==null)return;
        var progress=new Progress<DownloadProgress>(p=>{var percent=(int)(p.Received*100/Math.Max(1,p.Total));if(percent==Progress||_download!=null)return;Progress=percent;Notify($"正在下载 {_release.Version} · {Progress}%（{p.Received/1048576d:F1} / {p.Total/1048576d:F1} MB）");});
        _download=await _client.DownloadAsync(_asset,Path.Combine(UpdateRoot,_release.Version.ToString()),progress,token);
        Notify($"新版 {_release.Version} 已下载完成，点击「更新并重启」即可安装。");
    }
    private static string UpdateRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoLex","updates");
    internal static bool IsMsiInstallation()
    {
        try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\SuperRaine\MemoLex\Installer");return key?.GetValue("InstallDirectory") is string path&&Path.GetFullPath(path).TrimEnd('\\').Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}catch{return false;}
    }
    internal async Task InstallAsync(Window owner)
    {
        if(!CanInstall||_release==null||_asset==null||_download==null)return;
        if(MessageBox.Show(owner,$"更新到 {_release.Version} 并重启墨墨搜词？\n账号、历史记录和个人助记会保留。","安装更新",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
        _busy=true;Changed?.Invoke();
        try
        {
            if(!await GitHubUpdateClient.ValidFileAsync(_download,_asset)){_download=null;throw new UpdateException("已下载文件发生变化，请重新下载。");}
            var job=Path.Combine(UpdateRoot,"apply-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
            var portable=_asset.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase);string[] files=[];
            if(portable)files=(await Task.Run(()=>GitHubUpdateClient.ExtractPortable(_download,Path.Combine(job,"stage")))).ToArray();
            var appDir=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if(!File.Exists(Path.Combine(appDir,"MemoLex.exe"))||appDir==Path.GetPathRoot(appDir)?.TrimEnd('\\'))throw new UpdateException("无法确认程序安装目录，请从发布页手动更新。");
            var manifest=new {AppDirectory=appDir,Package=_download,Sha256=_asset.Sha256,Portable=portable,Files=files,ProcessId=Environment.ProcessId,ProcessStart=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks};
            File.WriteAllText(Path.Combine(job,"job.json"),JsonSerializer.Serialize(manifest));
            using var source=typeof(UpdateManager).Assembly.GetManifestResourceStream("MemoLex.update.ps1")??throw new UpdateException("更新组件缺失。");
            using(var reader=new StreamReader(source))File.WriteAllText(Path.Combine(job,"update.ps1"),await reader.ReadToEndAsync(),new System.Text.UTF8Encoding(true));
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=job};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(job,"update.ps1")})start.ArgumentList.Add(arg);
            using var helper=Process.Start(start)??throw new UpdateException("无法启动更新程序。");
            var wait=Stopwatch.StartNew();
            while(!File.Exists(Path.Combine(job,"ready")))
            {
                if(helper.HasExited||wait.Elapsed>TimeSpan.FromSeconds(20))throw new UpdateException("未能启动更新，软件仍可正常使用。请前往下载页面手动安装。");
                await Task.Delay(100);
            }
            // Exit without the normal close-to-tray prompt only after the helper is running.
            _exit();
        }
        catch(Exception error){Notify(error is UpdateException?error.Message:"无法开始安装，请打开发布页手动更新。");}
        finally{_busy=false;Changed?.Invoke();}
    }
    internal FrameworkElement BuildPanel(Window owner)
    {
        var panel=new StackPanel();
        panel.Children.Add(InputDialog.Text("当前版本 "+_current+" · Windows x64"));
        var enabled=new CheckBox{Content="自动检查并下载更新",IsChecked=_preferences.AutomaticUpdates,Margin=new Thickness(0,20,0,12)};panel.Children.Add(enabled);
        var choices=new[]{new Frequency("每次启动","startup"),new Frequency("每 6 小时","6h"),new Frequency("每天","daily"),new Frequency("每周","weekly")};
        panel.Children.Add(InputDialog.Text("检查更新间隔"));var frequency=new ComboBox{ItemsSource=choices,SelectedItem=choices.FirstOrDefault(c=>c.Value==_preferences.UpdateFrequency)??choices[2],Margin=new Thickness(0,8,0,12),MinWidth=180,MinHeight=37,HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(frequency);
        panel.Children.Add(InputDialog.Text("这些选项立即生效。下载完成后由你选择重启安装。关闭自动更新仍可手动检查。"));
        var state=InputDialog.Text(Status);state.Margin=new Thickness(0,24,0,12);panel.Children.Add(state);
        var bar=new ProgressBar{Minimum=0,Maximum=100,Height=5,Margin=new Thickness(0,0,0,16)};panel.Children.Add(bar);
        var last=InputDialog.Text("");panel.Children.Add(last);
        var buttons=new WrapPanel{Margin=new Thickness(0,14,0,16)};panel.Children.Add(buttons);
        Button Button(string title,string name,Action action){var button=new Button{Content=title,Margin=new Thickness(0,0,8,10)};button.Click+=(_,_)=>action();buttons.Children.Add(button);owner.RegisterName(name,button);return button;}
        var check=Button("手动检查更新","CheckUpdatesButton",async()=>await CheckAsync());
        var download=Button("下载更新","DownloadUpdateButton",async()=>await DownloadAsync());
        var install=Button("更新并重启","InstallUpdateButton",async()=>await InstallAsync(owner));
        var cancel=Button("取消当前操作","CancelUpdateButton",Cancel);
        Button("前往下载页面","UpdateReleasePage",()=>{try{Process.Start(new ProcessStartInfo(_release?.Page.AbsoluteUri??GitHubUpdateClient.Repository+"/releases/latest"){UseShellExecute=true});}catch{state.Text="无法打开浏览器。";}});
        var notes=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed};panel.Children.Add(notes);
        void Render(){state.Text=Status;bar.Value=Progress;bar.Visibility=_busy?Visibility.Visible:Visibility.Collapsed;check.IsEnabled=!Busy;download.IsEnabled=CanDownload;install.IsEnabled=CanInstall;cancel.Visibility=Busy?Visibility.Visible:Visibility.Collapsed;frequency.IsEnabled=enabled.IsChecked==true;last.Text=_preferences.LastUpdateCheck is {} at?"上次检查："+at.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"还未检查";notes.Text=_release?.Notes??"";notes.Visibility=notes.Text.Length>0?Visibility.Visible:Visibility.Collapsed;}
        enabled.Checked+=(_,_)=>{_preferences.AutomaticUpdates=true;PolicyChanged();};enabled.Unchecked+=(_,_)=>{_preferences.AutomaticUpdates=false;PolicyChanged();};
        frequency.SelectionChanged+=(_,_)=>{_preferences.UpdateFrequency=((Frequency)frequency.SelectedItem).Value;PolicyChanged();};
        panel.Loaded+=(_,_)=>{Changed+=Render;Render();};panel.Unloaded+=(_,_)=>Changed-=Render;
        owner.RegisterName("AutomaticUpdates",enabled);owner.RegisterName("UpdateFrequency",frequency);owner.RegisterName("UpdateStatus",state);Render();return panel;
    }
    private sealed record Frequency(string Label,string Value){public override string ToString()=>Label;}
    public void Dispose(){_disposed=true;_timer.Stop();_operation?.Cancel();_client.Dispose();}
}
