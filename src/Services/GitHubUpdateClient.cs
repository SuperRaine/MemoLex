using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoLex.Services;

public sealed record UpdateAsset(string Name,Uri Url,long Size,string Sha256);
public sealed record UpdateRelease(Version Version,Uri Page,string Notes,UpdateAsset? Installer,UpdateAsset? Portable);
public sealed record DownloadProgress(long Received,long Total,string? Source=null);

public sealed class UpdateException(string message,HttpStatusCode? statusCode=null):Exception(message){public HttpStatusCode? StatusCode {get;}=statusCode;}

public sealed partial class GitHubUpdateClient:IDisposable
{
    public const string Repository="https://github.com/SuperRaine/MemoLex";
    internal const string Latest="https://api.github.com/repos/SuperRaine/MemoLex/releases/latest";
    private const long MaxPackage=2L*1024*1024*1024;
    private readonly HttpClient _http;
    public GitHubUpdateClient():this(new HttpClientHandler{AllowAutoRedirect=false}){}
    internal GitHubUpdateClient(HttpMessageHandler handler)
    {
        _http=new(handler){Timeout=Timeout.InfiniteTimeSpan};
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/"+(typeof(GitHubUpdateClient).Assembly.GetName().Version?.ToString(3)??"1.1.1"));
    }
    public static bool TryVersion(string? text,out Version version)
    {
        version=new(0,0,0);
        return text!=null&&Regex.IsMatch(text,@"^v?\d{1,5}\.\d{1,5}\.\d{1,5}$",RegexOptions.CultureInvariant)&&Version.TryParse(text.TrimStart('v'),out version!);
    }
    public static bool CheckDue(bool enabled,string frequency,DateTimeOffset? lastCheck,DateTimeOffset now,bool startup=false)
    {
        if(!enabled)return false;
        if(frequency=="startup")return startup;
        var interval=frequency switch{"6h"=>TimeSpan.FromHours(6),"weekly"=>TimeSpan.FromDays(7),_=>TimeSpan.FromDays(1)};
        return lastCheck==null||lastCheck>now||now-lastCheck>=interval;
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri,CancellationToken token,bool head=false,System.Net.Http.Headers.RangeHeaderValue? range=null)
    {
        for(var redirects=0;redirects<=5;redirects++)
        {
            if(!AllowedUpdateUri(uri))throw new UpdateException("更新下载地址不受支持。");
            using var request=new HttpRequestMessage(head?HttpMethod.Head:HttpMethod.Get,uri);
            request.Headers.Range=range;
            if(uri.Host=="api.github.com")request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var connection=CancellationTokenSource.CreateLinkedTokenSource(token);connection.CancelAfter(ConnectionTimeout);
            var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,connection.Token);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location=response.Headers.Location;response.Dispose();
                if(location==null)throw new UpdateException("更新下载跳转无效。");
                uri=location.IsAbsoluteUri?location:new Uri(uri,location);continue;
            }
            if(response.IsSuccessStatusCode)return response;
            var status=response.StatusCode;response.Dispose();
            throw new UpdateException(status==HttpStatusCode.NotFound?"尚无可用的公开版本。":status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests?"GitHub 检查次数暂时受限，请稍后重试。":"无法连接 GitHub 更新服务，请稍后重试。",status);
        }
        throw new UpdateException("更新下载跳转过多。");
    }
    private async Task<string> ReadTextAsync(Uri uri,int limit,CancellationToken token)
    {
        using var response=await GetAsync(uri,token);
        if(response.Content.Headers.ContentLength>limit)throw new UpdateException("更新信息过大。");
        await using var input=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();var buffer=new byte[8192];
        while(true){var n=await input.ReadAsync(buffer,token);if(n==0)break;if(memory.Length+n>limit)throw new UpdateException("更新信息过大。");memory.Write(buffer,0,n);}
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }
    public Task<UpdateRelease> GetLatestAsync(CancellationToken token=default)=>GetLatestCoreAsync(null,token);
    public Task<UpdateRelease> GetLatestAsync(Version current,CancellationToken token=default)=>GetLatestCoreAsync(current,token);
    private async Task<UpdateRelease?> ReadVersionHeaderAsync(CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Head,Repository+"/releases/latest");
            using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))return null;
            var location=response.Headers.Location;
            if(location==null)return null;
            var page=location.IsAbsoluteUri?location:new Uri(new Uri(Repository),location);
            const string prefix="/SuperRaine/MemoLex/releases/tag/";
            if(page.Scheme!="https"||page.Host!="github.com"||!page.IsDefaultPort||page.UserInfo.Length>0||page.Query.Length>0||page.Fragment.Length>0||!page.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal)||!TryVersion(page.AbsolutePath[prefix.Length..],out var version))throw new UpdateException("GitHub 最新版本地址无效。");
            return new(version,page,"",null,null);
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){return null;}
        catch(HttpRequestException){return null;}
    }
    private async Task<UpdateRelease> GetLatestCoreAsync(Version? current,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var requests=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var headerTask=current==null?Task.FromResult<UpdateRelease?>(null):ReadVersionHeaderAsync(requests.Token);
        var apiTask=ReadApiReleaseAsync(current,requests.Token);
        try
        {
            await Task.WhenAny((Task)headerTask,apiTask);
            if(headerTask.IsCompletedSuccessfully&&headerTask.Result is {} latest&&latest.Version<=current)return latest;
            try{return await apiTask;}
            catch(Exception error)when(!timeout.IsCancellationRequested&&error is UpdateException or HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                UpdateRelease? header=null;
                try{header=await headerTask;}catch(Exception failure)when(failure is UpdateException or HttpRequestException or OperationCanceledException){}
                return await GetLatestFromPageAsync(current,header,timeout.Token);
            }
        }
        finally
        {
            requests.Cancel();
            try{await Task.WhenAll((Task)headerTask,apiTask);}catch(Exception error)when(error is UpdateException or HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException){}
        }
    }
    private async Task<UpdateRelease> ReadApiReleaseAsync(Version? current,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var json=await ReadTextAsync(new(Latest),2*1024*1024,timeout.Token);
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("tag_name",out var tag)||!TryVersion(tag.GetString(),out var version)||root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())throw new UpdateException("没有可用的正式更新版本。");
        var page=new Uri(Repository+"/releases/tag/"+tag.GetString());
        if(current!=null&&version<=current)return new(version,page,"",null,null);
        var assets=root.GetProperty("assets").EnumerateArray().ToArray();
        UpdateAsset? Asset(string name)
        {
            var matches=assets.Where(a=>a.GetProperty("name").GetString()==name).ToArray();if(matches.Length!=1)return null;
            var a=matches[0];var expected=Repository+"/releases/download/"+tag.GetString()+"/"+name;
            if(a.GetProperty("browser_download_url").GetString()!=expected||!a.GetProperty("size").TryGetInt64(out var size)||size is <=0 or >MaxPackage)throw new UpdateException("更新附件信息无效。");
            var digest=a.TryGetProperty("digest",out var d)&&d.ValueKind==JsonValueKind.String?d.GetString()??"":"";
            var hash=digest.StartsWith("sha256:")?digest[7..]:"";
            return new(name,new(expected),size,Regex.IsMatch(hash,"^[0-9a-fA-F]{64}$")?hash:"");
        }
        var stem="MemoLex-"+version+"-Windows-x64";
        var installer=Asset(stem+"-Setup.msi");var portable=Asset(stem+".zip");
        if(installer==null&&portable==null)throw new UpdateException("此版本尚未提供 Windows 更新包。");
        if(installer?.Sha256==""||portable?.Sha256=="")
        {
            var sums=Asset("SHA256SUMS.txt")??throw new UpdateException("此版本缺少校验信息，请到发布页查看。");
            var content=await ReadTextAsync(sums.Url,65536,timeout.Token);
            string Hash(string name)
            {
                var matches=content.Split('\n').Select(line=>Regex.Match(line.Trim(),@"^([a-fA-F0-9]{64})\s+\*?(.+)$"))
                    .Where(m=>m.Success&&m.Groups[2].Value==name).ToArray();
                return matches.Length==1?matches[0].Groups[1].Value:throw new UpdateException("更新包没有唯一有效的校验值。");
            }
            if(installer?.Sha256=="")installer=installer with{Sha256=Hash(installer.Name)};
            if(portable?.Sha256=="")portable=portable with{Sha256=Hash(portable.Name)};
        }
        var notes=root.TryGetProperty("body",out var body)&&body.ValueKind==JsonValueKind.String?body.GetString()??"":"";
        return new(version,page,notes.Length>16000?notes[..16000]:notes,installer,portable);
    }
    private async Task<UpdateRelease> GetLatestFromPageAsync(Version? current,UpdateRelease? header,CancellationToken token)
    {
        // GitHub's public release page remains usable when a shared IP exhausts the REST quota.
        var uri=header?.Page;
        if(uri==null)
        {
            using var pageResponse=await GetAsync(new(Repository+"/releases/latest"),token);
            uri=pageResponse.RequestMessage?.RequestUri;
        }
        const string prefix="/SuperRaine/MemoLex/releases/tag/";
        if(uri==null||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal)||!TryVersion(uri.AbsolutePath[prefix.Length..],out var version))throw new UpdateException("无法从 GitHub 发布页确认最新版本。");
        if(current!=null&&version<=current)return new(version,uri,"",null,null);
        var tag=uri.AbsolutePath[prefix.Length..];
        var html=await ReadTextAsync(new(Repository+"/releases/expanded_assets/"+tag),2*1024*1024,token);
        async Task<UpdateAsset?> Asset(string name)
        {
            var relative="/SuperRaine/MemoLex/releases/download/"+tag+"/"+name;
            var rows=Regex.Matches(html,@"<li\b[^>]*>.*?</li>",RegexOptions.Singleline|RegexOptions.CultureInvariant)
                .Select(m=>m.Value).Where(row=>row.Contains("href=\""+relative+"\"",StringComparison.Ordinal)).ToArray();
            if(rows.Length==0)return null;
            if(rows.Length!=1)throw new UpdateException("GitHub 发布附件不唯一。");
            var hash=Regex.Match(rows[0],"value=\"sha256:([a-fA-F0-9]{64})\"",RegexOptions.CultureInvariant);
            if(!hash.Success)throw new UpdateException("GitHub 发布附件缺少校验值。");
            var url=new Uri("https://github.com"+relative);var size=await ReadAssetSizeAsync(url,token);
            return new(name,url,size,hash.Groups[1].Value);
        }
        var stem="MemoLex-"+version+"-Windows-x64";
        var installerTask=Asset(stem+"-Setup.msi");var portableTask=Asset(stem+".zip");
        await Task.WhenAll(installerTask,portableTask);var installer=await installerTask;var portable=await portableTask;
        if(installer==null&&portable==null)throw new UpdateException("此公开版本没有可用的 Windows 更新包。");
        return new(version,uri,"可打开 GitHub 发布页查看此版本的详细说明。",installer,portable);
    }
    public static async Task<bool> ValidFileAsync(string path,UpdateAsset asset,CancellationToken token=default)
    {
        if(!File.Exists(path)||new FileInfo(path).Length!=asset.Size)return false;
        await using var file=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(file,token)).Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase);
    }
    // Extraction happens before closing the app, so corrupt archives leave the running copy intact.
    public static IReadOnlyList<string> ExtractPortable(string archive,string destination)
    {
        var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;Directory.CreateDirectory(root);
        using var zip=ZipFile.OpenRead(archive);var files=new List<(ZipArchiveEntry Entry,string Relative)>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var item in zip.Entries)
        {
            var path=item.FullName.Replace('\\','/');if(path=="MemoLex/"&&item.Name.Length==0)continue;
            if(!path.StartsWith("MemoLex/",StringComparison.Ordinal)||path.Contains(':')||path.Split('/').Any(p=>p is ".." or ".")||((item.ExternalAttributes>>16)&0xF000)==0xA000)throw new UpdateException("更新压缩包包含无效路径。");
            var relative=path[8..];if(relative.Length==0||relative.Split('/').Any(p=>p.Length==0)&&item.Name.Length>0)throw new UpdateException("更新压缩包路径有误。");
            var full=Path.GetFullPath(Path.Combine(root,relative));if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new UpdateException("更新文件超出目标目录。");
            if(item.Name.Length==0)continue;
            if(!seen.Add(relative)||relative.Split('/').Any(p=>p.EndsWith('.')||p.EndsWith(' ')||Regex.IsMatch(p,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))||Path.GetFileName(relative).Equals("token.dpapi",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(relative).Equals("preferences.json",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(relative).Equals("mnemonics.user.json",StringComparison.OrdinalIgnoreCase))throw new UpdateException("更新压缩包包含重复路径或个人数据。");
            total+=item.Length;if(total>4L*1024*1024*1024||files.Count>=20000)throw new UpdateException("更新压缩包解压后过大。");
            files.Add((item,relative));
        }
        if(!seen.Contains("MemoLex.exe")||!seen.Contains("MemoLex.dll"))throw new UpdateException("更新包缺少主程序。");
        foreach(var file in files){var path=Path.Combine(root,file.Relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);file.Entry.ExtractToFile(path,false);}
        return files.Select(f=>f.Relative).ToArray();
    }
    public void Dispose()=>_http.Dispose();
}
