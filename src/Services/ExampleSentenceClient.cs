using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using MemoLex.Core;

namespace MemoLex.Services;

public sealed record PaperTypeChoice(string Value, string Label)
{
    public static IReadOnlyList<PaperTypeChoice> All { get; } = new[] {
        new PaperTypeChoice("ALL", "全部考试"), new PaperTypeChoice("POSTGRADUATE", "考研"),
        new PaperTypeChoice("CET4", "大学四级"), new PaperTypeChoice("CET6", "大学六级"),
        new PaperTypeChoice("COLLEGE_ENTRANCE_EXAM", "高考") };
}
public sealed record ExampleSentence(string Id, string English, string Translation, string Source,
    string? OptionLabel, string ContextEnglish, string ContextTranslation, IReadOnlySet<string> MatchedWords, IReadOnlySet<string>? MatchedTranslations = null,bool TranslationAligned=true)
{
    public bool IsOption => OptionLabel != null;
}
public sealed record ExamplePage(IReadOnlyList<ExampleSentence> Items, int Total, int Offset, int ReceivedCount)
{
    public bool HasMore => ReceivedCount > 0 && Offset + ReceivedCount < Total;
}
public sealed class ExampleLookupException(string message) : Exception(message);

/// <summary>Public exam search. Deliberately has no access to a user's TokenStore.</summary>
public sealed class ExampleSentenceClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly Dictionary<string, (DateTimeOffset Time, ExamplePage Page)> _cache = new();
    private readonly TimeSpan _minimumInterval;
    private long _lastRequest;
    private bool _disposed;
    public ExampleSentenceClient() : this(new HttpClientHandler { AllowAutoRedirect = false }, TimeSpan.FromMilliseconds(350)) { }
    internal ExampleSentenceClient(HttpMessageHandler handler, TimeSpan minimumInterval)
    {
        _minimumInterval = minimumInterval;
        _http = new HttpClient(handler) { BaseAddress = new Uri("https://lookup.maimemo.com/api/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/1.4");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }
    public async Task<ExamplePage> SearchAsync(string keyword, string paperType = "ALL", int offset = 0, int limit = 10, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        keyword = keyword.Trim();
        if (keyword.Length is 0 or > 100 || keyword.Any(char.IsControl)) throw new ArgumentException("请输入有效的查询单词。");
        if (!PaperTypeChoice.All.Any(type => type.Value == paperType)) throw new ArgumentException("请选择有效的考试类型。");
        if (offset < 0 || limit is < 1 or > 50) throw new ArgumentException("例句分页参数无效。");
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"{keyword.ToLowerInvariant()}\n{paperType}\n{offset}\n{limit}";
        await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(10)) return cached.Page;
            }
            if (_lastRequest != 0)
            {
                var delay = _minimumInterval - Stopwatch.GetElapsedTime(_lastRequest);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                _lastRequest = Stopwatch.GetTimestamp();
                using var response = await _http.GetAsync($"search?offset={offset}&limit={limit}&keyword={Uri.EscapeDataString(keyword)}&paper_type={paperType}", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new ExampleLookupException("例句查询过于频繁，请稍后重试。");
                if (!response.IsSuccessStatusCode) throw new ExampleLookupException("例句服务暂时无法连接，请稍后重试。");
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
                var page = ParsePage(document.RootElement, offset);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_cache)
                {
                    if (_cache.Count >= 64) _cache.Remove(_cache.MinBy(item => item.Value.Time).Key);
                    _cache[key] = (DateTimeOffset.UtcNow, page);
                }
                return page;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ExampleLookupException("例句查询超时，请点击重试。"); }
            catch (HttpRequestException) { throw new ExampleLookupException("网络暂时不可用，请点击重试。"); }
            catch (IOException) { throw new ExampleLookupException("例句数据未接收完整，请点击重试。"); }
            catch (JsonException) { throw new ExampleLookupException("例句返回格式异常，请稍后重试。"); }
        }
        finally { _requests.Release(); }
    }

    private static ExamplePage ParsePage(JsonElement root, int offset)
    {
        if (Property(root, "success").ValueKind != JsonValueKind.True) throw new ExampleLookupException("例句服务暂时不可用，请稍后重试。");
        var data = Property(root, "data");
        var phrases = Property(data, "phrases");
        var total = Property(data, "total");
        if (total.ValueKind == JsonValueKind.Object) total = Property(total, "value");
        if (phrases.ValueKind != JsonValueKind.Array || !total.TryGetInt32Safe(out var totalCount) || totalCount < 0)
            throw new ExampleLookupException("例句返回格式异常，请稍后重试。");
        var result = new List<ExampleSentence>();
        foreach (var phrase in phrases.EnumerateArray())
        {
            var context = Property(phrase, "context");
            var english = ExampleText.CleanMarkup(Text(phrase, "phrase_en"));
            var contextEnglish = ExampleText.CleanMarkup(Text(context, "phrase"));
            if (english.Length == 0) english = contextEnglish;
            if (english.Length == 0) continue;
            var translation = ExampleText.CleanMarkup(Text(phrase, "phrase_zh"));
            var contextTranslation = ExampleText.CleanMarkup(Text(context, "translation"));
            string? optionLabel = null;
            var options = Property(context, "options");
            if (options.ValueKind == JsonValueKind.Array)
                foreach (var option in options.EnumerateArray())
                    if (ExampleText.CleanMarkup(Text(option, "phrase")).Equals(english, StringComparison.OrdinalIgnoreCase))
                    {
                        optionLabel = Text(option, "option");
                        if (optionLabel.Length == 0) optionLabel = "题目选项";
                        if (translation.Length == 0) translation = ExampleText.CleanMarkup(Text(option, "translation"));
                        break;
                    }
            var translationAligned=true;
            if (translation.Length == 0 && optionLabel == null){translation=contextTranslation;translationAligned=english.Equals(contextEnglish,StringComparison.Ordinal);}
            var highlight = Property(Property(phrase, "highlight"), "context");
            var highlightedText = Text(highlight, "phrase");
            var highlightedOptions = Property(highlight, "options");
            if (highlightedOptions.ValueKind == JsonValueKind.Array)
                highlightedText += string.Join(" ", highlightedOptions.EnumerateArray().Select(option => Text(option, "phrase")));
            result.Add(new ExampleSentence(Text(phrase, "phrase_id"), english, translation,
                ExampleText.CleanMarkup(Text(phrase, "source")), optionLabel,
                optionLabel != null && contextEnglish != english ? contextEnglish : "",
                optionLabel != null ? contextTranslation : "", ExampleText.HighlightedWords(highlightedText),
                ExampleText.HighlightedPhrases(Text(highlight,"translation")),translationAligned));
        }
        return new ExamplePage(result, totalCount, offset, phrases.GetArrayLength());
    }
    private static JsonElement Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static string Text(JsonElement element, string name) => Property(element, name).ValueKind == JsonValueKind.String ? Property(element, name).GetString() ?? "" : "";
    public int CachedCount { get { lock (_cache) return _cache.Count; } }
    public int ClearCache() { lock (_cache) { var count = _cache.Count; _cache.Clear(); return count; } }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

internal static class JsonExampleExtensions
{
    public static bool TryGetInt32Safe(this JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
}
