using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemoLex.Core;

namespace MemoLex.Services;

internal sealed record OnlineDefinition(string Meaning, string? SourceUrl, string Provider, string? DictionarySource = null)
{
    public string SourceLabel => (SourceUrl == null ? "联网翻译" : "维基词典译文") + " · " + Provider
        + (DictionarySource == null ? "" : " · " + DictionarySource);

    public string? SourceAttribution => SourceUrl == null ? null : $"维基词典（CC BY-SA 4.0）\n{SourceUrl}";
}

internal sealed class OnlineDictionaryClient : IDisposable
{
    private const string Wiki = "https://en.wiktionary.org/";
    private const string PrimaryEndpoint = Wiki + "api/rest_v1/page/definition/";
    private const string BackupName = "FreeDictionaryAPI.com";
    private const string BackupEndpoint = "https://freedictionaryapi.com/api/v1/entries/en/";
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
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 256 * 1024
        };
        var version = typeof(OnlineDictionaryClient).Assembly.GetName().Version?.ToString(3) ?? "2.3.0";
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
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        try
        {
            var lower = word.ToLowerInvariant();
            var joined = lower.StartsWith("non-", StringComparison.Ordinal) && lower.Length > 4
                && lower[4..].All(char.IsAsciiLetter) ? "non" + lower[4..] : lower;
            var titles = new[] { word, lower, joined }.Distinct(StringComparer.Ordinal).ToArray();
            var match = await FindSensesAsync(titles, useBackup: false, token).ConfigureAwait(false);
            if (match is not { HasPartOfSpeech: true })
            {
                var backup = await FindSensesAsync(titles, useBackup: true, token).ConfigureAwait(false);
                if (backup is { HasPartOfSpeech: true } || match == null) match = backup;
            }

            Gloss[] groups = match == null
                ? [new("", word)]
                : match.Senses.GroupBy(sense => sense.PartOfSpeech)
                    .Select(group => new Gloss(group.Key, string.Join('\n', group.Select(sense => sense.Text))))
                    .ToArray();
            var result = await _translation.TranslateBatchAsync(groups.Select(group => group.Text).ToArray(),
                cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // A translation can merge or split lines, so keep each category tied to its own request item.
            var pieces = groups.Zip(result.Texts, (group, text) => new Gloss(group.PartOfSpeech,
                ChineseReverseSearch.ContainsChinese(text) ? DictionaryEntry.FormatDisplayMeaning($"{group.PartOfSpeech} {text}") : ""))
                .Where(piece => piece.Text.Length > 0).ToArray();
            if (pieces.Length == 0) return null;

            var source = match == null ? null : Wiki + "wiki/" + Uri.EscapeDataString(match.Title.Replace(' ', '_')) + "#English";
            var definition = new OnlineDefinition(string.Join('\n', pieces.Select(piece => piece.Text)),
                source, result.Provider, match?.FromBackup == true ? BackupName : null);
            // A temporary dictionary outage should not turn a direct translation into the cached word entry.
            if (!pieces.Any(piece => piece.PartOfSpeech.Length > 0)) return definition;
            if (_cache.Count >= 256 && _cache.Keys.FirstOrDefault() is { } previous) _cache.TryRemove(previous, out _);
            return _cache.GetOrAdd(word, definition);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (TranslationException) { return null; }
    }

    private async Task<DictionaryMatch?> FindSensesAsync(string[] titles, bool useBackup, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(useBackup ? 5 : 3));
        DictionaryMatch? unlabeled = null;
        try
        {
            foreach (var title in titles)
            {
                budget.Token.ThrowIfCancellationRequested();
                var senses = await GetSensesAsync(title, useBackup, budget.Token).ConfigureAwait(false);
                if (senses.Count == 0) continue;
                var match = new DictionaryMatch(title, senses, useBackup);
                if (match.HasPartOfSpeech) return match;
                unlabeled ??= match;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return unlabeled;
    }

    private async Task<List<Gloss>> GetSensesAsync(string title, bool useBackup, CancellationToken cancellationToken)
    {
        try
        {
            var escaped = Uri.EscapeDataString(useBackup ? title : title.Replace(' ', '_'));
            var endpoint = useBackup ? BackupEndpoint : PrimaryEndpoint;
            using var reply = await _http.GetAsync(endpoint + escaped, cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccessStatusCode) return [];
            using var stream = await reply.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ReadSenses(json.RootElement, title, useBackup);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException) { return []; }
    }

    private static List<Gloss> ReadSenses(JsonElement root, string title, bool useBackup)
    {
        var senses = new List<Gloss>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(useBackup ? "entries" : "en", out var english)
            || english.ValueKind != JsonValueKind.Array)
            return senses;
        if (useBackup && (!root.TryGetProperty("word", out var word) || word.ValueKind != JsonValueKind.String
            || !string.Equals(word.GetString(), title, StringComparison.Ordinal)))
            return senses;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in english.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(useBackup ? "senses" : "definitions", out var definitions)
                || definitions.ValueKind != JsonValueKind.Array || useBackup && !IsEnglishEntry(entry)) continue;
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

    private static bool IsEnglishEntry(JsonElement entry) =>
        entry.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.Object
        && language.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() == "en";

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
    private sealed record DictionaryMatch(string Title, List<Gloss> Senses, bool FromBackup)
    {
        public bool HasPartOfSpeech => Senses.Any(sense => sense.PartOfSpeech.Length > 0);
    }

    public void Dispose() => _http.Dispose();
}
