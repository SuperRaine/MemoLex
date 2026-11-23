using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using MemoLex.Core;

namespace MemoLex.Services;

internal enum EnglishAccent { British, American }

internal sealed class BaiduPronunciationClient : IDisposable
{
    private const string Endpoint = "https://fanyi.baidu.com/gettts";
    private static readonly Regex Headword = new(@"\A[A-Za-zÀ-ÖØ-öø-ÿĀ-ž][A-Za-zÀ-ÖØ-öø-ÿĀ-ž .’'\-]{0,99}\z");
    private readonly HttpClient _http;

    public BaiduPronunciationClient()
        : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }

    internal BaiduPronunciationClient(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
            MaxResponseContentBufferSize = 512 * 1024
        };
        _http.DefaultRequestHeaders.Accept.ParseAdd("audio/mpeg");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/" + typeof(BaiduPronunciationClient).Assembly.GetName().Version?.ToString(3));
    }

    public async Task<byte[]?> DownloadAsync(string spelling, EnglishAccent accent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var word = ExampleText.NormalizeLookup(spelling).Trim();
        if (!Headword.IsMatch(word)) return null;
        var language = accent == EnglishAccent.British ? "uk" : "en";
        var url = $"{Endpoint}?lan={language}&text={Uri.EscapeDataString(word)}&spd=3&source=web";
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode ||
                !string.Equals(response.Content.Headers.ContentType?.MediaType, "audio/mpeg", StringComparison.OrdinalIgnoreCase))
                return null;
            var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return audio.Length >= 4 && (audio.AsSpan().StartsWith("ID3"u8) || (audio[0] == 0xff && (audio[1] & 0xe0) == 0xe0))
                ? audio : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is HttpRequestException or IOException) { return null; }
    }

    public void Dispose() => _http.Dispose();
}
