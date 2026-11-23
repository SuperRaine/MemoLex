using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MemoLex.Core;

namespace MemoLex.Services;

internal enum EnglishAccent { British, American }

internal sealed class PronunciationClient : IDisposable
{
    private const string Baidu = "https://fanyi.baidu.com/gettts";
    private const string ExampleVoice = "https://memolex.de5.net/api/example-audio";
    private const string BackupVoice = "https://www.memolex.de5.net/api/example-audio?voice=backup";
    private const string DirectVoice = "https://api.deepgram.com/v1/speak?model=aura-2-arcas-en&encoding=mp3";
    private static readonly TimeSpan BackupDelay = TimeSpan.FromSeconds(1.75);
    private static readonly TimeSpan SentenceTimeout = TimeSpan.FromSeconds(9);
    private const int CachedSentenceLimit = 32;
    private const int MaxCachedAudioBytes = 512 * 1024;
    private static readonly Regex Headword = new(@"\A[A-Za-zÀ-ÖØ-öø-ÿĀ-ž][A-Za-zÀ-ÖØ-öø-ÿĀ-ž .’'\-]{0,99}\z");
    private static readonly Regex SentenceWhitespace = new(@"\s+");
    private static readonly Regex ExamSpeakerLabel = new(@"\A[QA][:：]\s*(?=[A-Z])", RegexOptions.CultureInvariant);
    private readonly HttpClient _http;
    private readonly Dictionary<string, byte[]> _sentenceCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cachedSentences = new();

    public PronunciationClient()
        : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }

    internal PronunciationClient(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        _http.DefaultRequestHeaders.Accept.ParseAdd("audio/mpeg");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/" + typeof(PronunciationClient).Assembly.GetName().Version?.ToString(3));
    }

    public async Task<byte[]?> DownloadAsync(string spelling, EnglishAccent accent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var word = ExampleText.NormalizeLookup(spelling).Trim();
        if (!Headword.IsMatch(word)) return null;
        var language = accent == EnglishAccent.British ? "uk" : "en";
        using var request = BaiduRequest(word, language, speed: 3);
        return await DownloadAudioAsync(request, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    internal async Task<byte[]?> DownloadSentenceAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sentence = ExamSpeakerLabel.Replace(SentenceWhitespace.Replace(text.Trim(), " "), "");
        if (sentence.Length is 0 or > 2000 || !sentence.Any(char.IsAsciiLetter)) return null;

        lock (_sentenceCache)
            if (_sentenceCache.TryGetValue(sentence, out var cached)) return cached;

        using var requests = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var primary = DownloadPrimarySentenceAsync(sentence, requests.Token);
            var headStart = Task.Delay(BackupDelay, requests.Token);
            var first = await Task.WhenAny(primary, headStart).ConfigureAwait(false);
            if (first == primary && await primary.ConfigureAwait(false) is { } quickAudio)
                return CacheSentence(sentence, quickAudio);

            cancellationToken.ThrowIfCancellationRequested();
            var backup = DownloadHostedSentenceAsync(BackupVoice, sentence, requests.Token);
            var winner = await Task.WhenAny(primary, backup).ConfigureAwait(false);
            var other = winner == primary ? backup : primary;
            var audio = await winner.ConfigureAwait(false) ?? await other.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return audio == null ? null : CacheSentence(sentence, audio);
        }
        finally { requests.Cancel(); }
    }

    private byte[] CacheSentence(string sentence, byte[] audio)
    {
        if (audio.Length > MaxCachedAudioBytes) return audio;
        lock (_sentenceCache)
        {
            if (!_sentenceCache.TryAdd(sentence, audio)) return audio;
            _cachedSentences.Enqueue(sentence);
            if (_cachedSentences.Count > CachedSentenceLimit) _sentenceCache.Remove(_cachedSentences.Dequeue());
        }
        return audio;
    }

    private async Task<byte[]?> DownloadPrimarySentenceAsync(string sentence, CancellationToken cancellationToken)
    {
        var key = Environment.GetEnvironmentVariable("MEMOLEX_DEEPGRAM_API_KEY")?.Trim();
        if (key is { Length: > 0 and <= 256 } && key.All(character => character is > ' ' and < '\u007F'))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, DirectVoice)
            {
                Content = JsonContent.Create(new { text = sentence })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
            var audio = await DownloadAudioAsync(request, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (audio != null) return audio;
        }

        return await DownloadHostedSentenceAsync(ExampleVoice, sentence, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> DownloadHostedSentenceAsync(string url, string sentence, CancellationToken cancellationToken)
    {
        using var hosted = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { text = sentence })
        };
        return await DownloadAudioAsync(hosted, SentenceTimeout, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage BaiduRequest(string text, string language, int speed) =>
        new(HttpMethod.Get, $"{Baidu}?lan={language}&text={Uri.EscapeDataString(text)}&spd={speed}&source=web");

    private async Task<byte[]?> DownloadAudioAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response = await _http.SendAsync(request, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode ||
                !string.Equals(response.Content.Headers.ContentType?.MediaType, "audio/mpeg", StringComparison.OrdinalIgnoreCase))
                return null;
            var audio = await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
            return audio.Length >= 4 && (audio.AsSpan().StartsWith("ID3"u8) || (audio[0] == 0xff && (audio[1] & 0xe0) == 0xe0))
                ? audio : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is HttpRequestException or IOException) { return null; }
    }

    public void Dispose() => _http.Dispose();
}
