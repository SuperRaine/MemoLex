using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemoLex.Core;

namespace MemoLex.Services;

internal sealed record OnlineDefinition(string Meaning, string? SourceUrl, string Provider);

internal sealed class OnlineDictionaryClient : IDisposable
{
    private const string Wiki = "https://en.wiktionary.org/";
    private static readonly Regex LookupWord = new(@"^[a-zA-Z][a-zA-Z .'’\-]{0,99}$");
    private static readonly Regex Spaces = new(@"\s+");
    private readonly HttpClient _http;
    private readonly TranslationClient _translation;
    private readonly ConcurrentDictionary<string, OnlineDefinition> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lookupSlots = new(3);

    public OnlineDictionaryClient(TranslationClient translation)
        : this(translation, new HttpClientHandler { AllowAutoRedirect = false }) { }

    internal OnlineDictionaryClient(TranslationClient translation, HttpMessageHandler handler)
    {
        _translation = translation;
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(Wiki),
            Timeout = TimeSpan.FromSeconds(3),
            MaxResponseContentBufferSize = 256 * 1024
        };
        var version = typeof(OnlineDictionaryClient).Assembly.GetName().Version?.ToString(3) ?? "1.16.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"MemoLex/{version} (https://github.com/SuperRaine/MemoLex)");
    }

    public async Task<OnlineDefinition?> LookupAsync(string spelling, CancellationToken cancellationToken)
    {
        var word = ExampleText.NormalizeLookup(spelling).Trim();
        if (!LookupWord.IsMatch(word)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(word, out var cached)) return cached;

        await _lookupSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(word, out cached)) return cached;
            return await LookupUncachedAsync(word, cancellationToken).ConfigureAwait(false);
        }
        finally { _lookupSlots.Release(); }
    }

    private async Task<OnlineDefinition?> LookupUncachedAsync(string word, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(17));
        var token = deadline.Token;
        try
        {
            var lower = word.ToLowerInvariant();
            var joined = lower.StartsWith("non-", StringComparison.Ordinal) && lower.Length > 4
                && lower[4..].All(char.IsAsciiLetter) ? "non" + lower[4..] : lower;
            var title = word;
            List<Gloss> senses = [];
            foreach (var candidate in new[] { word, lower, joined }.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                senses = await GetSensesAsync(candidate, token).ConfigureAwait(false);
                if (senses.Count == 0) continue;
                title = candidate;
                break;
            }

            var text = senses.Count == 0 ? word : string.Join('\n', senses.Select(sense => sense.Text));
            token.ThrowIfCancellationRequested();
            var result = await _translation.TranslateAsync(text, cancellationToken: token).ConfigureAwait(false);
            var translated = result.Text.Trim();
            token.ThrowIfCancellationRequested();
            if (!ChineseReverseSearch.ContainsChinese(translated)) return null;

            var lines = translated.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (senses.Count == lines.Length && senses.Count > 0)
                translated = string.Join('\n', lines.Select((line, index) => (senses[index].PartOfSpeech + " " + line).TrimStart()));
            translated = DictionaryEntry.FormatListMeaning(translated);
            var source = senses.Count == 0 ? null : Wiki + "wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')) + "#English";
            var definition = new OnlineDefinition(translated, source, result.Provider);
            if (_cache.Count >= 256 && _cache.Keys.FirstOrDefault() is { } previous) _cache.TryRemove(previous, out _);
            return _cache.GetOrAdd(word, definition);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (TranslationException) { return null; }
    }

    private async Task<List<Gloss>> GetSensesAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            var escaped = Uri.EscapeDataString(title.Replace(' ', '_'));
            using var reply = await _http.GetAsync("api/rest_v1/page/definition/" + escaped, cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccessStatusCode) return [];
            using var stream = await reply.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ReadSenses(json.RootElement);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException) { return []; }
    }

    private static List<Gloss> ReadSenses(JsonElement root)
    {
        var senses = new List<Gloss>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("en", out var english) || english.ValueKind != JsonValueKind.Array)
            return senses;

        var remaining = 420;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in english.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("definitions", out var definitions)
                || definitions.ValueKind != JsonValueKind.Array) continue;
            var part = entry.TryGetProperty("partOfSpeech", out var value) && value.ValueKind == JsonValueKind.String
                ? Abbreviate(value.GetString()) : "";
            foreach (var definition in definitions.EnumerateArray())
            {
                if (definition.ValueKind != JsonValueKind.Object || !definition.TryGetProperty("definition", out var html)
                    || html.ValueKind != JsonValueKind.String) continue;
                var text = Spaces.Replace(ExampleText.CleanMarkup(html.GetString()), " ");
                if (text.Length < 2 || text.Length > remaining || !text.Any(char.IsAsciiLetter) || !seen.Add(text)) continue;
                senses.Add(new Gloss(part, text));
                remaining -= text.Length + 1;
                if (senses.Count == 3) return senses;
            }
        }
        return senses;
    }

    private static string Abbreviate(string? part) => part?.ToLowerInvariant() switch
    {
        "noun" or "proper noun" => "n.",
        "verb" => "v.",
        "adjective" => "adj.",
        "adverb" => "adv.",
        "pronoun" => "pron.",
        "preposition" => "prep.",
        "conjunction" => "conj.",
        "interjection" => "int.",
        "determiner" => "det.",
        _ => ""
    };

    private sealed record Gloss(string PartOfSpeech, string Text);
    public void Dispose() => _http.Dispose();
}
