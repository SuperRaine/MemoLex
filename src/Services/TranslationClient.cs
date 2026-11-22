using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MemoLex.Services;

public class TranslationException(string message) : Exception(message);
public sealed record TranslationResult(string Text, string Provider);

public sealed class TranslationClient : IDisposable
{
    private const int ResponseLimit = 1024 * 1024;
    private const int BackupConcurrency = 4;
    private const string DeepL = "DeepL";
    private const string Backup = "MyMemory";

    private readonly HttpClient _http;
    private readonly object _state = new();
    private readonly SemaphoreSlim _backupSlots = new(BackupConcurrency);
    private readonly Dictionary<CacheKey, TranslationResult> _cache = new();
    private readonly Func<string?> _deepLKey;
    private readonly TimeSpan _deepLTimeout, _retryCooldown;
    private DateTimeOffset _retryAfter;
    private string? _failedCredential;

    public string PreferredProvider => string.IsNullOrEmpty(_deepLKey()) ? Backup : DeepL;

    public TranslationClient() : this(BundledTranslationKey.Get) { }

    public TranslationClient(Func<string?> keyProvider)
        : this(new HttpClientHandler { AllowAutoRedirect = false }, keyProvider) { }

    internal TranslationClient(HttpMessageHandler handler, Func<string?>? keyProvider = null,
        TimeSpan? deepLTimeout = null, TimeSpan? retryCooldown = null)
    {
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.mymemory.translated.net/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _deepLKey = keyProvider ?? (() => null);
        _deepLTimeout = deepLTimeout ?? TimeSpan.FromSeconds(6);
        _retryCooldown = retryCooldown ?? TimeSpan.FromSeconds(25);
        var version = typeof(TranslationClient).Assembly.GetName().Version?.ToString(3);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/" + version);
    }

    public async Task<TranslationResult> TranslateAsync(string text, string pair = "en|zh-CN",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 5000)
            throw new ArgumentException("请输入 1–5000 个字符的段落。");
        if (pair is not ("en|zh-CN" or "zh-CN|en"))
            throw new ArgumentException("请选择英译中或中译英。");
        cancellationToken.ThrowIfCancellationRequested();

        var secret = _deepLKey();
        if (!string.IsNullOrEmpty(secret))
        {
            var credential = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
            var primary = new CacheKey(pair, text, credential);
            if (Cached(primary) is { } previous) return previous;
            if (CanUseDeepL(credential))
            {
                try
                {
                    var translated = await TranslateDeepLAsync(text, pair, secret, cancellationToken).ConfigureAwait(false);
                    return Remember(primary, new TranslationResult(translated, DeepL));
                }
                catch (DeepLUnavailable error)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PauseDeepL(credential, error.RetryAfter ?? _retryCooldown);
                }
            }
        }

        var fallback = new CacheKey(pair, text, "");
        if (Cached(fallback) is { } remembered) return remembered;
        var backupText = await TranslateBackupAsync(text, pair, cancellationToken).ConfigureAwait(false);
        return Remember(fallback, new TranslationResult(backupText, Backup));
    }

    private TranslationResult? Cached(CacheKey key)
    {
        lock (_state) return _cache.GetValueOrDefault(key);
    }

    private TranslationResult Remember(CacheKey key, TranslationResult result)
    {
        lock (_state)
        {
            if (_cache.Count >= 64) _cache.Remove(_cache.Keys.First());
            return _cache[key] = result;
        }
    }

    private bool CanUseDeepL(string credential)
    {
        lock (_state) return _failedCredential != credential || DateTimeOffset.UtcNow >= _retryAfter;
    }

    private void PauseDeepL(string credential, TimeSpan interval)
    {
        lock (_state)
        {
            var until = DateTimeOffset.UtcNow + interval;
            if (_failedCredential == credential && until <= _retryAfter) return;
            _failedCredential = credential;
            _retryAfter = until;
        }
    }

    private async Task<string> TranslateDeepLAsync(string text, string pair, string secret,
        CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(_deepLTimeout);
        try
        {
            var endpoint = secret.EndsWith(":fx", StringComparison.Ordinal)
                ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", secret);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                text = new[] { text },
                source_lang = pair == "en|zh-CN" ? "EN" : "ZH",
                target_lang = pair == "en|zh-CN" ? "ZH-HANS" : "EN-US",
                preserve_formatting = true
            }), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                TimeSpan? retryAfter = (int)response.StatusCode switch
                {
                    401 or 403 or 456 => TimeSpan.FromMinutes(5),
                    429 when response.Headers.RetryAfter?.Delta is { } delay =>
                        TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 10, 120)),
                    _ => null
                };
                throw new DeepLUnavailable(retryAfter);
            }

            using var json = await ReadJsonAsync(response, timeout.Token).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("translations", out var results)
                || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1
                || results[0].ValueKind != JsonValueKind.Object || !results[0].TryGetProperty("text", out var value)
                || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new DeepLUnavailable();

            cancellation.ThrowIfCancellationRequested();
            return value.GetString()!;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new DeepLUnavailable();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            throw new DeepLUnavailable();
        }
    }

    private async Task<string> TranslateBackupAsync(string text, string pair, CancellationToken cancellation)
    {
        var segments = SplitSegments(text);
        var translations = new string[segments.Count];
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, segments.Count),
                new ParallelOptions { MaxDegreeOfParallelism = BackupConcurrency, CancellationToken = budget.Token },
                async (index, token) =>
                {
                    var content = segments[index].Trim();
                    if (content.Length == 0) return;
                    translations[index] = await TranslateSegmentAsync(content, pair, token).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TranslationException("翻译服务未能及时返回，请重试。");
        }

        cancellation.ThrowIfCancellationRequested();
        var result = new StringBuilder();
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (translations[index] is not { } translated)
            {
                result.Append(segment);
                continue;
            }
            result.Append(segment.AsSpan(0, segment.Length - segment.TrimStart().Length));
            if (pair == "zh-CN|en" && result.Length > 0 && char.IsAsciiLetterOrDigit(result[^1])
                && char.IsAsciiLetterOrDigit(translated[0])) result.Append(' ');
            result.Append(translated);
            result.Append(segment.AsSpan(segment.TrimEnd().Length));
        }
        return result.ToString();
    }

    private async Task<string> TranslateSegmentAsync(string content, string pair, CancellationToken cancellation)
    {
        await _backupSlots.WaitAsync(cancellation).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await _http.GetAsync(
                "get?q=" + Uri.EscapeDataString(content) + "&langpair=" + Uri.EscapeDataString(pair),
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                throw new TranslationException("备用翻译服务已限流或达到额度，请稍后重试。");
            if (!response.IsSuccessStatusCode)
                throw new TranslationException("备用翻译服务暂时不可用，请稍后重试。");

            using var json = await ReadJsonAsync(response, timeout.Token).ConfigureAwait(false);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new TranslationException("备用翻译服务未返回有效译文，请重试。");
            var status = root.TryGetProperty("responseStatus", out var code) ? code.ToString() : "";
            if (status is "403" or "429"
                || root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
                throw new TranslationException("备用翻译服务已限流或达到额度，请稍后重试。");
            if (status != "200" || !root.TryGetProperty("responseData", out var data)
                || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("translatedText", out var translated)
                || translated.ValueKind != JsonValueKind.String)
                throw new TranslationException("备用翻译服务未返回有效译文，请重试。");

            var decoded = WebUtility.HtmlDecode(translated.GetString());
            if (string.IsNullOrWhiteSpace(decoded))
                throw new TranslationException("备用翻译服务未返回有效译文，请重试。");
            return decoded;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TranslationException("翻译服务未能及时返回，请重试。");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            throw new TranslationException("暂未获取到译文，请检查网络后重试。");
        }
        finally
        {
            _backupSlots.Release();
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        await response.Content.LoadIntoBufferAsync(ResponseLimit).WaitAsync(token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
    }

    internal static IReadOnlyList<string> SplitSegments(string text)
    {
        var result = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var bytes = 0;
            var end = start;
            var boundary = -1;
            foreach (var rune in text.AsSpan(start).EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 450) break;
                bytes += rune.Utf8SequenceLength;
                end += rune.Utf16SequenceLength;
                if (rune.Value is ' ' or '\n' or '.' or '?' or '!' or '。' or '？' or '！') boundary = end;
            }
            if (end < text.Length && boundary > start + (end - start) / 2) end = boundary;
            result.Add(text[start..end]);
            start = end;
        }
        return result;
    }

    private sealed class DeepLUnavailable(TimeSpan? retryAfter = null) : Exception
    {
        internal TimeSpan? RetryAfter { get; } = retryAfter;
    }

    private readonly record struct CacheKey(string Pair, string Text, string Credential);
    public void Dispose() => _http.Dispose();
}
