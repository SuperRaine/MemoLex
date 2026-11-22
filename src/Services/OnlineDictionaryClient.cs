using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemoLex.Core;

namespace MemoLex.Services;

internal sealed record OnlineDefinition(string Meaning, string? SourceUrl, string Provider)
{
    public string SourceLabel => (SourceUrl == null ? "联网翻译" : "维基词典译文") + " · " + Provider;

    public string? SourceAttribution => SourceUrl == null ? null : $"维基词典（CC BY-SA 4.0）\n{SourceUrl}";
}

internal sealed class OnlineDictionaryClient : IDisposable
{
    private const string Wiki = "https://en.wiktionary.org/";
    private const int MaximumSenses = 3;
    private const int GlossCharacterBudget = 420;
    private static readonly Regex LookupWord = new(@"^[a-zA-Z][a-zA-Z .'’\-]{0,99}$");
    private static readonly Regex Spaces = new(@"\s+");
    private static readonly Regex NonDefinitionMarkup = new(@"<(style|script)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
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
        var version = typeof(OnlineDictionaryClient).Assembly.GetName().Version?.ToString(3) ?? "1.18.0";
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

            Gloss[] groups = senses.Count == 0
                ? [new("", word)]
                : senses.GroupBy(sense => sense.PartOfSpeech)
                    .Select(group => new Gloss(group.Key, string.Join('\n', group.Select(sense => sense.Text))))
                    .ToArray();
            var result = await _translation.TranslateBatchAsync(groups.Select(group => group.Text).ToArray(),
                cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // A translation can merge or split lines, so keep each category tied to its own request item.
            var pieces = groups.Zip(result.Texts, (group, text) => ChineseReverseSearch.ContainsChinese(text)
                ? DictionaryEntry.FormatDisplayMeaning($"{group.PartOfSpeech} {text}") : "");
            var translated = string.Join('\n', pieces.Where(piece => piece.Length > 0));
            if (translated.Length == 0) return null;
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
                var markup = NonDefinitionMarkup.Replace(html.GetString()!, "");
                var text = Spaces.Replace(ExampleText.CleanMarkup(markup), " ");
                if (text.Length < 2 || text.Length > GlossCharacterBudget || !text.Any(char.IsAsciiLetter)
                    || !seen.Add(part + "\n" + text)) continue;
                senses.Add(new Gloss(part, text));
            }
        }

        var groups = senses.GroupBy(sense => sense.PartOfSpeech).ToArray();
        // Use the first definition of each known part of speech before filling in additional meanings.
        var preferred = groups.Where(group => group.Key.Length > 0).Select(group => group.First())
            .Concat(groups.SelectMany(group => group.Key.Length > 0 ? group.Skip(1) : group));
        var selected = new List<Gloss>();
        var remaining = GlossCharacterBudget;
        foreach (var sense in preferred)
        {
            if (sense.Text.Length > remaining) continue;
            selected.Add(sense);
            remaining -= sense.Text.Length + 1;
            if (selected.Count == MaximumSenses) break;
        }
        return selected;
    }

    private static string Abbreviate(string? part) => part?.Trim().ToLowerInvariant() switch
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
        "numeral" => "num.",
        "article" => "art.",
        "participle" => "ptcp.",
        "particle" => "part.",
        "abbreviation" or "acronym" or "initialism" => "abbr.",
        "contraction" => "contr.",
        "phrase" or "idiom" or "proverb" => "phr.",
        "prefix" => "pref.",
        "suffix" => "suf.",
        _ => ""
    };

    private sealed record Gloss(string PartOfSpeech, string Text);
    public void Dispose() => _http.Dispose();
}
