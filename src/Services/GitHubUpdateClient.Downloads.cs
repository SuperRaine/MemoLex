using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace MemoLex.Services;

internal sealed record UpdateDownloadSource(string Id,string Label,string Prefix)
{
    internal Uri Address(Uri original)=>Prefix.Length==0?original:new Uri(Prefix+original.AbsoluteUri);
    public override string ToString()=>Label;
}

public sealed partial class GitHubUpdateClient
{
    internal static IReadOnlyList<UpdateDownloadSource> DownloadSources {get;}=System.Array.AsReadOnly(new[]
    {
        new UpdateDownloadSource("auto","自动选择",""),
        new UpdateDownloadSource("ghproxy","GH-Proxy","https://gh-proxy.com/"),
        new UpdateDownloadSource("ghfast","GHFast","https://ghfast.top/"),
        new UpdateDownloadSource("github","GitHub 官方","")
    });
    internal TimeSpan ConnectionTimeout {get;init;}=TimeSpan.FromSeconds(8);
    internal TimeSpan DownloadIdleTimeout {get;init;}=TimeSpan.FromSeconds(20);

    private static bool AllowedUpdateUri(Uri uri)
    {
        if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0)return false;
        if(uri.Host is "api.github.com" or "github.com"||uri.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase))return true;
        return uri.Host is "gh-proxy.com" or "ghfast.top"
            &&uri.AbsolutePath.StartsWith("/https://github.com/SuperRaine/MemoLex/releases/download/",StringComparison.Ordinal);
    }
    private static bool CanonicalDownload(UpdateAsset asset)=>asset.Url.Scheme=="https"&&asset.Url.Host=="github.com"&&asset.Url.IsDefaultPort
        &&asset.Url.UserInfo.Length==0&&asset.Url.Query.Length==0&&asset.Url.Fragment.Length==0
        &&Regex.IsMatch(asset.Url.AbsolutePath,@"^/SuperRaine/MemoLex/releases/download/v?\d+\.\d+\.\d+/[^/]+$",RegexOptions.CultureInvariant)
        &&Uri.UnescapeDataString(asset.Url.Segments[^1])==asset.Name;

    private static void ValidateRange(HttpResponseMessage response,long offset,long size)
    {
        if(response.StatusCode==HttpStatusCode.PartialContent)
        {
            var range=response.Content.Headers.ContentRange;
            if(range is not {HasRange:true,HasLength:true}||range.From!=offset||range.Length!=size||range.To>=size||range.To<range.From)
                throw new UpdateException("下载源返回的续传信息无效，正在尝试备用源。");
        }
        else if(response.StatusCode!=HttpStatusCode.OK)throw new UpdateException("下载源暂时不可用。");
    }
    private async Task<int> ReadDownloadAsync(Stream input,Memory<byte> buffer,CancellationToken token)
    {
        using var idle=CancellationTokenSource.CreateLinkedTokenSource(token);idle.CancelAfter(DownloadIdleTimeout);
        try{return await input.ReadAsync(buffer,idle.Token);}
        catch(IOException error){throw new HttpRequestException("更新下载连接中断。",error);}
    }
    private async Task<bool> ProbeAsync(UpdateDownloadSource source,UpdateAsset asset,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(ConnectionTimeout);
        try
        {
            var length=(int)Math.Min(65536,asset.Size);
            using var response=await GetAsync(source.Address(asset.Url),timeout.Token,range:new RangeHeaderValue(0,length-1));
            ValidateRange(response,0,asset.Size);
            await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);var bytes=new byte[length];var received=0;
            while(received<bytes.Length)
            {
                var count=await ReadDownloadAsync(input,bytes.AsMemory(received),timeout.Token);if(count==0)break;received+=count;
            }
            if(received<Math.Min(8,length))return false;
            return asset.Name.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)
                ?received>=8&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1})
                :received>=4&&bytes[0]==0x50&&bytes[1]==0x4B&&bytes[2]==3&&bytes[3]==4;
        }
        catch(Exception error)when(!token.IsCancellationRequested&&error is UpdateException or HttpRequestException or OperationCanceledException){return false;}
    }
    private async Task<long> ReadAssetSizeAsync(Uri url,CancellationToken token)
    {
        // The checksum still comes from GitHub's official release page. Obtaining
        // the exact length must not require the direct asset CDN to be reachable.
        using var probing=CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task<long?> TrySource(UpdateDownloadSource source)
        {
            try
            {
                var official=source.Id=="github";
                using var response=await GetAsync(source.Address(url),probing.Token,head:official,range:official?null:new RangeHeaderValue(0,7));
                var length=response.StatusCode==HttpStatusCode.PartialContent?response.Content.Headers.ContentRange?.Length:response.Content.Headers.ContentLength;
                if(length is not {} size||size is <=0 or >MaxPackage||response.Content.Headers.ContentType?.MediaType=="text/html")return null;
                if(!official)
                {
                    await using var input=await response.Content.ReadAsStreamAsync(probing.Token);var bytes=new byte[8];var received=0;
                    while(received<8){var count=await ReadDownloadAsync(input,bytes.AsMemory(received),probing.Token);if(count==0)return null;received+=count;}
                    var valid=url.AbsolutePath.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)
                        ?bytes.AsSpan().SequenceEqual(new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1})
                        :bytes[0]==0x50&&bytes[1]==0x4B&&bytes[2]==3&&bytes[3]==4;
                    if(!valid)return null;
                }
                return size;
            }
            catch(Exception error)when(!token.IsCancellationRequested&&error is UpdateException or HttpRequestException or OperationCanceledException){return null;}
        }
        var pending=DownloadSources.Where(s=>s.Id!="auto").Select(TrySource).ToList();
        try
        {
            while(pending.Count>0)
            {
                var completed=await Task.WhenAny(pending);pending.Remove(completed);
                if(await completed is {} size)return size;
            }
        }
        finally
        {
            probing.Cancel();
            try{await Task.WhenAll(pending);}catch(Exception error)when(error is OperationCanceledException or HttpRequestException or UpdateException){}
        }
        token.ThrowIfCancellationRequested();throw new UpdateException("所有下载源暂时不可用，无法确认更新附件大小。请稍后重试。");
    }
    private async Task<IReadOnlyList<UpdateDownloadSource>> OrderSourcesAsync(UpdateAsset asset,string preferred,IProgress<string>? status,CancellationToken token)
    {
        var sources=DownloadSources.Where(s=>s.Id!="auto").ToArray();
        if(DownloadSources.FirstOrDefault(s=>s.Id==preferred&&s.Id!="auto") is {} selected)
            return new[]{selected}.Concat(sources.Where(s=>s.Id!=selected.Id)).ToArray();
        status?.Report("正在检测可用的更新源…");
        using var probing=CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending=sources.Select(async source=>(Source:source,Available:await ProbeAsync(source,asset,probing.Token))).ToList();
        UpdateDownloadSource? fastest=null;
        try
        {
            while(pending.Count>0)
            {
                var completed=await Task.WhenAny(pending);pending.Remove(completed);var result=await completed;
                if(result.Available){fastest=result.Source;break;}
            }
        }
        finally
        {
            probing.Cancel();
            try{await Task.WhenAll(pending);}catch(Exception error)when(error is OperationCanceledException or HttpRequestException or UpdateException){}
        }
        token.ThrowIfCancellationRequested();
        return fastest==null?sources:new[]{fastest}.Concat(sources.Where(s=>s.Id!=fastest.Id)).ToArray();
    }

    public async Task<string> DownloadAsync(UpdateAsset asset,string directory,IProgress<DownloadProgress>? progress=null,CancellationToken token=default,string source="auto",IProgress<string>? status=null)
    {
        if(!Regex.IsMatch(asset.Sha256,"^[0-9a-fA-F]{64}$")||Path.GetFileName(asset.Name)!=asset.Name
            ||asset.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||asset.Size is <=0 or >MaxPackage||!CanonicalDownload(asset))
            throw new UpdateException("更新包校验信息不完整。");
        Directory.CreateDirectory(directory);var target=Path.Combine(directory,asset.Name);var partial=target+".part";
        if(File.Exists(target)&&await ValidFileAsync(target,asset,token))return target;
        if(File.Exists(partial)&&new FileInfo(partial).Length>=asset.Size)
        {
            if(await ValidFileAsync(partial,asset,token)){File.Move(partial,target,true);return target;}
            File.Delete(partial);
        }
        using var overall=CancellationTokenSource.CreateLinkedTokenSource(token);overall.CancelAfter(TimeSpan.FromMinutes(30));
        var ordered=await OrderSourcesAsync(asset,source,status,overall.Token);
        foreach(var candidate in ordered)
        {
            overall.Token.ThrowIfCancellationRequested();
            status?.Report("正在连接更新源："+candidate.Label+"…");
            try
            {
                var offset=File.Exists(partial)?new FileInfo(partial).Length:0;
                using var response=await GetAsync(candidate.Address(asset.Url),overall.Token,range:offset>0?new RangeHeaderValue(offset,null):null);
                ValidateRange(response,offset,asset.Size);
                if(response.StatusCode==HttpStatusCode.OK)offset=0; // A server may ignore Range; restart rather than append twice.
                var expected=asset.Size-offset;
                if(response.Content.Headers.ContentLength is {} length&&length!=expected)throw new UpdateException("下载源返回的文件大小不符。");
                status?.Report("正在下载 · "+candidate.Label);progress?.Report(new(offset,asset.Size,candidate.Label));
                await using(var output=new FileStream(partial,offset>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,65536,true))
                await using(var input=await response.Content.ReadAsStreamAsync(overall.Token))
                {
                    var buffer=new byte[65536];var total=offset;
                    while(true)
                    {
                        var count=await ReadDownloadAsync(input,buffer,overall.Token);if(count==0)break;
                        total+=count;if(total>asset.Size)throw new UpdateException("下载源返回的文件大小超出发布信息。");
                        await output.WriteAsync(buffer.AsMemory(0,count),overall.Token);progress?.Report(new(total,asset.Size,candidate.Label));
                    }
                    if(total!=asset.Size)throw new HttpRequestException("更新下载尚未完成。");
                }
                status?.Report("下载完成，正在校验更新包…");
                if(!await ValidFileAsync(partial,asset,overall.Token))
                {File.Delete(partial);throw new UpdateException("更新包校验未通过，正在尝试备用源。");}
                File.Move(partial,target,true);return target;
            }
            catch(Exception error)when(!overall.IsCancellationRequested&&error is UpdateException or HttpRequestException or OperationCanceledException)
            {status?.Report(candidate.Label+" 暂不可用，正在切换备用源…");}
        }
        throw new UpdateException("暂时无法完成下载，已尝试所有更新源。请稍后重试，已下载的部分会保留。");
    }
}
