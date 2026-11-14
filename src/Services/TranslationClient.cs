using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace MemoLex.Services;

public class TranslationException(string message) : Exception(message);
internal sealed class TranslationUnavailableException(string message):TranslationException(message);
public sealed class TranslationClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Dictionary<string,string> _cache=new();
    private readonly Func<string?> _deepLKey;
    private readonly TimeSpan _deepLTimeout,_retryCooldown;
    private DateTimeOffset _retryAfter;
    private string? _failedCredential;
    public string LastProvider {get;private set;}="MyMemory";
    public string PreferredProvider=>string.IsNullOrEmpty(_deepLKey())?"MyMemory":"DeepL";
    public TranslationClient() : this(new HttpClientHandler { AllowAutoRedirect=false },BundledTranslationKey.Get) { }
    public TranslationClient(Func<string?> keyProvider):this(new HttpClientHandler {AllowAutoRedirect=false},keyProvider){}
    internal TranslationClient(HttpMessageHandler handler,Func<string?>? keyProvider=null,TimeSpan? deepLTimeout=null,TimeSpan? retryCooldown=null)
    {
        _http=new HttpClient(handler) { BaseAddress=new Uri("https://api.mymemory.translated.net/"),Timeout=Timeout.InfiniteTimeSpan };
        _deepLKey=keyProvider??(()=>null);
        _deepLTimeout=deepLTimeout??TimeSpan.FromSeconds(10);_retryCooldown=retryCooldown??TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/"+(typeof(TranslationClient).Assembly.GetName().Version?.ToString(3)??"1.1.2"));
    }
    public async Task<string> TranslateAsync(string text,string pair="en|zh-CN",CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>5000)throw new ArgumentException("请输入 1–5000 个字符的段落。");
        if(pair is not ("en|zh-CN" or "zh-CN|en"))throw new ArgumentException("请选择英译中或中译英。");
        var secret=_deepLKey();
        var provider=string.IsNullOrEmpty(secret)?"MyMemory":"DeepL";
        var credential=secret==null?"":Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        var key=provider+":"+credential+"\n"+pair+"\n"+text;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LastProvider=provider;
            if(_cache.TryGetValue(key,out var cached))return cached;
            if(provider=="DeepL")
            {
                if(_failedCredential!=credential||DateTimeOffset.UtcNow>=_retryAfter)
                {
                    try
                    {
                        var translated=await TranslateDeepLAsync(text,pair,secret!,cancellationToken).ConfigureAwait(false);
                        _failedCredential=null;
                        if(_cache.Count>=32)_cache.Remove(_cache.Keys.First());
                        return _cache[key]=translated;
                    }
                    catch(TranslationUnavailableException){_failedCredential=credential;_retryAfter=DateTimeOffset.UtcNow+_retryCooldown;}
                }
                cancellationToken.ThrowIfCancellationRequested();LastProvider="MyMemory（DeepL 暂不可用）";
                key="MyMemory:\n"+pair+"\n"+text;
                if(_cache.TryGetValue(key,out var backup))return backup;
            }
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
    private async Task<string> TranslateDeepLAsync(string text,string pair,string secret,CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(_deepLTimeout);
        try
        {
            var endpoint=secret.EndsWith(":fx",StringComparison.Ordinal)?"https://api-free.deepl.com/v2/translate":"https://api.deepl.com/v2/translate";
            using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);
            request.Headers.Authorization=new AuthenticationHeaderValue("DeepL-Auth-Key",secret);
            request.Content=new StringContent(JsonSerializer.Serialize(new {text=new[]{text},source_lang=pair=="en|zh-CN"?"EN":"ZH",target_lang=pair=="en|zh-CN"?"ZH-HANS":"EN-US"}),Encoding.UTF8,"application/json");
            using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
            if((int)response.StatusCode is 408 or 429 or >=500)throw new TranslationUnavailableException("DeepL 暂时无法连接。");
            if(!response.IsSuccessStatusCode)throw new TranslationException((int)response.StatusCode switch
            {
                403=>"DeepL 授权暂不可用，请稍后重试或等待软件更新。",
                456=>"DeepL 翻译额度已用尽，请额度恢复后重试。",
                429=>"DeepL 请求过于频繁，请稍后重试。",
                _=>"DeepL 暂时无法翻译，请稍后重试。"
            });
            await response.Content.LoadIntoBufferAsync(1024*1024).WaitAsync(timeout.Token).ConfigureAwait(false);
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            if(!json.RootElement.TryGetProperty("translations",out var results)||results.ValueKind!=JsonValueKind.Array||results.GetArrayLength()!=1||!results[0].TryGetProperty("text",out var value)||value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(value.GetString()))throw new TranslationException("DeepL 未返回有效译文，请重试。");
            cancellation.ThrowIfCancellationRequested();return value.GetString()!;
        }
        catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){throw new TranslationUnavailableException("DeepL 翻译超时。");}
        catch(Exception error)when(error is HttpRequestException or IOException){throw new TranslationUnavailableException("DeepL 未能连接。");}
        catch(JsonException){throw new TranslationException("DeepL 未返回有效译文，请重试。");}
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
