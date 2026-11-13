using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MemoLex.Services;

public sealed class TranslationException(string message) : Exception(message);
public sealed class TranslationClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Dictionary<string,string> _cache=new();
    public TranslationClient() : this(new HttpClientHandler { AllowAutoRedirect=false }) { }
    internal TranslationClient(HttpMessageHandler handler)
    {
        _http=new HttpClient(handler) { BaseAddress=new Uri("https://api.mymemory.translated.net/"),Timeout=Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/1.4");
    }
    public async Task<string> TranslateAsync(string text,string pair="en|zh-CN",CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>5000)throw new ArgumentException("请输入 1–5000 个字符的段落。");
        if(pair is not ("en|zh-CN" or "zh-CN|en"))throw new ArgumentException("请选择英译中或中译英。");
        var key=pair+"\n"+text;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if(_cache.TryGetValue(key,out var cached))return cached;
            var result=new StringBuilder();
            foreach(var segment in SplitSegments(text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(string.IsNullOrWhiteSpace(segment)){result.Append(segment);continue;}
                var content=segment.Trim();
                result.Append(segment[..(segment.Length-segment.TrimStart().Length)]);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    using var response=await _http.GetAsync("get?q="+Uri.EscapeDataString(content)+"&langpair="+Uri.EscapeDataString(pair),HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
                    if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)throw new TranslationException("翻译服务额度已用尽或请求过于频繁，请稍后重试。");
                    if(!response.IsSuccessStatusCode)throw new TranslationException("翻译服务暂时无法连接，请稍后重试。");
                    await response.Content.LoadIntoBufferAsync(1024*1024).WaitAsync(timeout.Token).ConfigureAwait(false);
                    using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                    var root=document.RootElement;
                    var status=root.TryGetProperty("responseStatus",out var code)?code.ToString():"";
                    if(root.TryGetProperty("quotaFinished",out var quota)&&quota.ValueKind==JsonValueKind.True)throw new TranslationException("MyMemory 免费翻译额度已用尽，请次日重试。");
                    if(status is "403" or "429")throw new TranslationException("MyMemory 免费翻译额度已用尽，请稍后或次日重试。");
                    if(status!="200"||!root.TryGetProperty("responseData",out var data)||!data.TryGetProperty("translatedText",out var translated)||translated.ValueKind!=JsonValueKind.String)
                        throw new TranslationException("翻译服务未返回有效结果，请稍后重试。");
                    var translatedText=WebUtility.HtmlDecode(translated.GetString());
                    if(string.IsNullOrWhiteSpace(translatedText))throw new TranslationException("翻译服务返回了空结果，请重试。");
                    if(pair=="zh-CN|en"&&result.Length>0&&!char.IsWhiteSpace(result[^1])&&char.IsAsciiLetter(translatedText[0]))result.Append(' ');
                    result.Append(translatedText);
                }
                catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) {throw new TranslationException("段落翻译超时，请重试。");}
                catch(Exception error) when(error is HttpRequestException or IOException or JsonException) {throw new TranslationException("段落翻译未完成，请检查网络后重试。");}
                result.Append(segment[(segment.TrimEnd().Length)..]);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if(_cache.Count>=32)_cache.Remove(_cache.Keys.First());
            return _cache[key]=result.ToString();
        }
        finally{_gate.Release();}
    }
    internal static IReadOnlyList<string> SplitSegments(string text)
    {
        var result=new List<string>();var start=0;
        while(start<text.Length)
        {
            var bytes=0;var end=start;var boundary=-1;
            foreach(var rune in text.AsSpan(start).EnumerateRunes())
            {
                if(bytes+rune.Utf8SequenceLength>450)break;
                bytes+=rune.Utf8SequenceLength;end+=rune.Utf16SequenceLength;
                if(rune.Value is ' ' or '\n' or '.' or '?' or '!' or '。' or '？' or '！')boundary=end;
            }
            if(end<text.Length&&boundary>start+(end-start)/2)end=boundary;
            result.Add(text[start..end]);start=end;
        }
        return result;
    }
    public void Dispose()=>_http.Dispose();
}
