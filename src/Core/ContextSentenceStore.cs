using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record ContextSentenceItem(
    string Id,
    string Word,
    string Sentence,
    string Translation = "",
    string Source = "查词保存",
    string? MatchedForm = null,
    DateTimeOffset SavedAt = default,
    int ReviewCount = 0,
    DateTimeOffset? LastReviewedAt = null,
    string? CloudId = null,
    string Origin = "local",
    string? AccountKey = null,
    IReadOnlyList<string>? Tags = null)
{
    public bool IsCloud => string.Equals(Origin, "cloud", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(CloudId);
    public bool HasUnsyncedCloudEdit => !string.Equals(Origin, "cloud", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(CloudId);
}

public sealed record CloudPhraseRecord(
    string Id,
    string Sentence,
    string Translation = "",
    string Origin = "",
    IReadOnlyList<string>? Tags = null);

public static partial class ContextSentenceHelper
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "st", "vs", "etc", "eg", "ie", "fig", "no", "vol", "pp", "inc", "ltd", "co"
    };

    [GeneratedRegex(@"(?<=[\.\!\?。！？])\s+|\r?\n+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceSplitPattern();

    public static string ExtractSentenceForWord(string paragraph, string word, string? matchedForm = null)
    {
        if (string.IsNullOrWhiteSpace(paragraph)) return "";
        var clean = paragraph.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var sentences = SplitEnglishSentences(clean);
        var targets = new List<string>();
        if (!string.IsNullOrWhiteSpace(matchedForm)) targets.Add(matchedForm.Trim());
        if (!string.IsNullOrWhiteSpace(word) && !targets.Any(t => t.Equals(word.Trim(), StringComparison.OrdinalIgnoreCase)))
            targets.Add(word.Trim());

        foreach (var target in targets)
        {
            foreach (var sentence in sentences)
            {
                if (ContainsWord(sentence, target))
                    return TrimSentence(sentence);
            }
        }
        // Fallback: stem prefix match (e.g. addressed -> address)
        if (!string.IsNullOrWhiteSpace(word) && word.Trim().Length >= 4)
        {
            var stem = word.Trim().ToLowerInvariant();
            if (stem.Length > 4 && stem.EndsWith('e')) stem = stem[..^1];
            foreach (var sentence in sentences)
            {
                foreach (var part in ExampleText.Tokenize(sentence))
                {
                    if (part.LookupWord is { } lw && lw.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                        return TrimSentence(sentence);
                }
            }
        }
        return TrimSentence(sentences.FirstOrDefault() ?? clean);
    }

    public static string ExtractTranslationHint(string sourceParagraph, string fullTranslation, string selectedSentence)
    {
        if (string.IsNullOrWhiteSpace(fullTranslation)) return "";
        var cleanTranslation = fullTranslation.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var srcSentences = SplitEnglishSentences(sourceParagraph.Replace("\r\n", "\n").Replace('\r', '\n').Trim());
        var zhSentences = SplitChineseSentences(cleanTranslation);
        if (srcSentences.Count > 1 && zhSentences.Count == srcSentences.Count)
        {
            var idx = srcSentences.FindIndex(s => TrimSentence(s).Equals(selectedSentence, StringComparison.OrdinalIgnoreCase)
                || s.Contains(selectedSentence, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx < zhSentences.Count)
                return TrimTranslation(zhSentences[idx]);
        }
        return TrimTranslation(cleanTranslation);
    }

    public static bool ContainsWord(string sentence, string word)
    {
        if (string.IsNullOrWhiteSpace(sentence) || string.IsNullOrWhiteSpace(word)) return false;
        var target = ExampleText.NormalizeLookup(word.Trim());
        if (target.Contains(' '))
            return sentence.Contains(word.Trim(), StringComparison.OrdinalIgnoreCase);
        foreach (var part in ExampleText.Tokenize(sentence))
        {
            if (part.LookupWord != null && part.LookupWord.Equals(target, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string BlankOutWord(string sentence, string word, string? matchedForm = null)
    {
        if (string.IsNullOrWhiteSpace(sentence) || string.IsNullOrWhiteSpace(word)) return sentence;
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ExampleText.NormalizeLookup(word.Trim()) };
        if (!string.IsNullOrWhiteSpace(matchedForm)) targets.Add(ExampleText.NormalizeLookup(matchedForm.Trim()));

        var parts = ExampleText.Tokenize(sentence);
        var replaced = false;
        var output = new System.Text.StringBuilder(sentence.Length + 8);
        var stem = word.Trim().ToLowerInvariant();
        if (stem.Length > 4 && stem.EndsWith('e')) stem = stem[..^1];

        foreach (var part in parts)
        {
            if (part.LookupWord != null && (targets.Contains(part.LookupWord)
                || (!replaced && stem.Length >= 4 && part.LookupWord.StartsWith(stem, StringComparison.OrdinalIgnoreCase))))
            {
                output.Append("______");
                replaced = true;
            }
            else
            {
                output.Append(part.Text);
            }
        }
        return output.ToString();
    }

    private static List<string> SplitEnglishSentences(string text)
    {
        var raw = SentenceSplitPattern().Split(text)
            .Select(s => Regex.Replace(s.Trim(), @"\s+", " "))
            .Where(s => s.Length > 0)
            .ToList();
        if (raw.Count <= 1) return raw;

        var merged = new List<string>(raw.Count);
        foreach (var piece in raw)
        {
            if (merged.Count > 0 && EndsWithAbbreviation(merged[^1]))
                merged[^1] = merged[^1] + " " + piece;
            else
                merged.Add(piece);
        }
        return merged;
    }

    private static List<string> SplitChineseSentences(string text) =>
        Regex.Split(text, @"(?<=[。！？!?])\s*|\r?\n+")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static bool EndsWithAbbreviation(string sentence)
    {
        if (!sentence.EndsWith('.')) return false;
        var trimmed = sentence[..^1];
        var lastSpace = trimmed.LastIndexOfAny([' ', '\t', '(', '"', '\'']);
        var lastToken = lastSpace >= 0 ? trimmed[(lastSpace + 1)..] : trimmed;
        return lastToken.Length is >= 1 and <= 4 && (Abbreviations.Contains(lastToken) || lastToken.Length == 1 && char.IsLetter(lastToken[0]));
    }

    private static string TrimSentence(string sentence)
    {
        var clean = Regex.Replace(sentence.Trim(), @"\s+", " ");
        return clean.Length <= 320 ? clean : clean[..317].TrimEnd() + "…";
    }

    private static string TrimTranslation(string translation)
    {
        var clean = Regex.Replace(translation.Trim(), @"\s+", " ");
        return clean.Length <= 260 ? clean : clean[..257].TrimEnd() + "…";
    }

    public static IReadOnlyList<string> ParseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return Regex.Split(raw.Trim(), @"[,，、;；\s]+")
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 30 ? t[..30] : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }
}

public sealed class ContextSentenceStore
{
    public const int MaxItems = 800;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _path;
    private readonly object _gate = new();
    private List<ContextSentenceItem> _items = [];
    private bool _loaded;

    public ContextSentenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoLex", "context-sentences.json");
    }

    public int Count => CountForAccount(null, includeAllAccounts: true);

    public int CountForAccount(string? accountKey, bool includeAllAccounts = false)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (includeAllAccounts) return _items.Count;
            return _items.Count(item => IsVisibleTo(item, accountKey));
        }
    }

    public long FileSizeBytes
    {
        get
        {
            lock (_gate)
            {
                try { return File.Exists(_path) ? new FileInfo(_path).Length : 0; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
            }
        }
    }

    public int Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            var removed = _items.Count;
            _items.Clear();
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return removed;
        }
    }

    public ContextSentenceItem? FindById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            EnsureLoaded();
            return _items.FirstOrDefault(item => item.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<ContextSentenceItem> List(string? word = null, string? accountKey = null, bool includeAllAccounts = false)
    {
        lock (_gate)
        {
            EnsureLoaded();
            IEnumerable<ContextSentenceItem> query = includeAllAccounts
                ? _items
                : _items.Where(item => IsVisibleTo(item, accountKey));
            if (string.IsNullOrWhiteSpace(word))
                return query.ToArray();
            var target = word.Trim();
            return query.Where(item => item.Word.Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    public ContextSentenceItem? FindLatestForWord(string word, string? accountKey = null)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        lock (_gate)
        {
            EnsureLoaded();
            var target = word.Trim();
            return _items.FirstOrDefault(item => IsVisibleTo(item, accountKey) && item.Word.Equals(target, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool Contains(string word, string sentence, string? accountKey = null)
    {
        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(sentence)) return false;
        var w = word.Trim();
        var s = NormalizeSentenceKey(sentence);
        lock (_gate)
        {
            EnsureLoaded();
            return _items.Any(item => IsVisibleTo(item, accountKey)
                && item.Word.Equals(w, StringComparison.OrdinalIgnoreCase)
                && NormalizeSentenceKey(item.Sentence).Equals(s, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ContextSentenceItem Save(
        string word,
        string sentence,
        string translation = "",
        string source = "查词保存",
        string? matchedForm = null,
        string? accountKey = null,
        IReadOnlyList<string>? tags = null)
    {
        var cleanWord = CleanWord(word);
        var cleanSentence = CleanSentence(sentence);
        var cleanTranslation = CleanTranslation(translation);
        var cleanSource = CleanSource(source, "查词保存");
        var cleanForm = string.IsNullOrWhiteSpace(matchedForm) ? null : matchedForm.Trim();
        var cleanTags = CleanTags(tags);

        lock (_gate)
        {
            EnsureLoaded();
            var key = NormalizeSentenceKey(cleanSentence);
            var existingIdx = _items.FindIndex(item => IsVisibleTo(item, accountKey)
                && item.Word.Equals(cleanWord, StringComparison.OrdinalIgnoreCase)
                && NormalizeSentenceKey(item.Sentence).Equals(key, StringComparison.OrdinalIgnoreCase));
            ContextSentenceItem saved;
            if (existingIdx >= 0)
            {
                var prev = _items[existingIdx];
                saved = prev with
                {
                    Sentence = cleanSentence,
                    Translation = cleanTranslation.Length > 0 ? cleanTranslation : prev.Translation,
                    Source = cleanSource,
                    MatchedForm = cleanForm ?? prev.MatchedForm,
                    Tags = cleanTags.Count > 0 ? cleanTags : prev.Tags,
                    SavedAt = DateTimeOffset.UtcNow
                };
                _items.RemoveAt(existingIdx);
                _items.Insert(0, saved);
            }
            else
            {
                saved = new ContextSentenceItem(
                    Guid.NewGuid().ToString("N")[..12],
                    cleanWord,
                    cleanSentence,
                    cleanTranslation,
                    cleanSource,
                    cleanForm,
                    DateTimeOffset.UtcNow,
                    0,
                    null,
                    CloudId: null,
                    Origin: "local",
                    AccountKey: null,
                    Tags: cleanTags.Count > 0 ? cleanTags : null);
                _items.Insert(0, saved);
                TrimCapacityLocked();
            }
            PersistLocked();
            return saved;
        }
    }

    public ContextSentenceItem SaveEdit(
        string? id,
        string word,
        string sentence,
        string translation = "",
        string source = "自建例句",
        IReadOnlyList<string>? tags = null,
        string? cloudId = null,
        string? origin = null,
        string? accountKey = null,
        string? matchedForm = null)
    {
        var cleanWord = CleanWord(word);
        var cleanSentence = CleanSentence(sentence);
        var cleanTranslation = CleanTranslation(translation);
        var cleanSource = CleanSource(source, "自建例句");
        var cleanTags = CleanTags(tags);
        var cleanCloudId = string.IsNullOrWhiteSpace(cloudId) ? null : cloudId.Trim();
        var cleanAccountKey = string.IsNullOrWhiteSpace(accountKey) ? null : accountKey.Trim();
        var normalizedOrigin = string.Equals(origin, "cloud", StringComparison.OrdinalIgnoreCase) && cleanCloudId != null
            ? "cloud"
            : "local";

        lock (_gate)
        {
            EnsureLoaded();
            var idx = !string.IsNullOrWhiteSpace(id)
                ? _items.FindIndex(item => item.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase))
                : -1;
            if (idx < 0 && cleanCloudId != null && cleanAccountKey != null)
            {
                idx = _items.FindIndex(item => item.AccountKey == cleanAccountKey
                    && string.Equals(item.CloudId, cleanCloudId, StringComparison.Ordinal));
            }

            ContextSentenceItem saved;
            if (idx >= 0)
            {
                var prev = _items[idx];
                var resolvedCloudId = cleanCloudId ?? (prev.AccountKey == cleanAccountKey ? prev.CloudId : null);
                var resolvedAccount = resolvedCloudId != null ? (cleanAccountKey ?? prev.AccountKey) : null;
                var resolvedOrigin = normalizedOrigin == "cloud" && resolvedCloudId != null ? "cloud" : "local";
                var resolvedForm = !string.IsNullOrWhiteSpace(matchedForm)
                    ? matchedForm.Trim()
                    : (prev.MatchedForm != null && ContextSentenceHelper.ContainsWord(cleanSentence, prev.MatchedForm) ? prev.MatchedForm : null);
                saved = prev with
                {
                    Word = cleanWord,
                    Sentence = cleanSentence,
                    Translation = cleanTranslation,
                    Source = cleanSource,
                    MatchedForm = resolvedForm,
                    SavedAt = DateTimeOffset.UtcNow,
                    CloudId = resolvedCloudId,
                    Origin = resolvedOrigin,
                    AccountKey = resolvedAccount,
                    Tags = cleanTags.Count > 0 ? cleanTags : null
                };
                _items.RemoveAt(idx);
                _items.Insert(0, saved);
            }
            else
            {
                var resolvedAccount = cleanCloudId != null ? cleanAccountKey : null;
                saved = new ContextSentenceItem(
                    cleanCloudId != null ? "cloud:" + cleanCloudId : Guid.NewGuid().ToString("N")[..12],
                    cleanWord,
                    cleanSentence,
                    cleanTranslation,
                    cleanSource,
                    string.IsNullOrWhiteSpace(matchedForm) ? null : matchedForm.Trim(),
                    DateTimeOffset.UtcNow,
                    0,
                    null,
                    CloudId: cleanCloudId,
                    Origin: normalizedOrigin,
                    AccountKey: resolvedAccount,
                    Tags: cleanTags.Count > 0 ? cleanTags : null);
                _items.Insert(0, saved);
            }

            // Remove accidental exact duplicates for the same word/account after editing.
            var key = NormalizeSentenceKey(saved.Sentence);
            _items.RemoveAll(item => !item.Id.Equals(saved.Id, StringComparison.OrdinalIgnoreCase)
                && IsVisibleTo(item, saved.AccountKey ?? cleanAccountKey)
                && item.Word.Equals(saved.Word, StringComparison.OrdinalIgnoreCase)
                && NormalizeSentenceKey(item.Sentence).Equals(key, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(item.CloudId) || string.Equals(item.CloudId, saved.CloudId, StringComparison.Ordinal)));

            TrimCapacityLocked();
            PersistLocked();
            return saved;
        }
    }

    public IReadOnlyList<ContextSentenceItem> MergeCloud(string word, IReadOnlyList<CloudPhraseRecord> cloudPhrases, string accountKey)
    {
        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(accountKey))
            return List(word, accountKey);
        var cleanWord = word.Trim();
        var cleanAccount = accountKey.Trim();

        lock (_gate)
        {
            EnsureLoaded();
            var wordItems = _items.Where(item => item.Word.Equals(cleanWord, StringComparison.OrdinalIgnoreCase)).ToList();
            var existingByCloudId = wordItems
                .Where(item => item.AccountKey == cleanAccount && !string.IsNullOrWhiteSpace(item.CloudId))
                .GroupBy(item => item.CloudId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var existingLocalBySentence = wordItems
                .Where(item => string.IsNullOrWhiteSpace(item.CloudId) && string.IsNullOrWhiteSpace(item.AccountKey))
                .GroupBy(item => NormalizeSentenceKey(item.Sentence), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var incomingCloudIds = new HashSet<string>(StringComparer.Ordinal);
            var promotedLocalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mergedCloudItems = new List<ContextSentenceItem>(cloudPhrases.Count);

            foreach (var phrase in cloudPhrases)
            {
                if (phrase == null || string.IsNullOrWhiteSpace(phrase.Id) || string.IsNullOrWhiteSpace(phrase.Sentence))
                    continue;
                var cloudId = phrase.Id.Trim();
                if (!incomingCloudIds.Add(cloudId)) continue;

                var sentence = CleanSentenceOrFallback(phrase.Sentence);
                if (sentence.Length == 0) continue;
                var translation = CleanTranslation(phrase.Translation);
                var source = CleanSource(phrase.Origin, "墨墨云端");
                var tags = CleanTags(phrase.Tags);

                if (existingByCloudId.TryGetValue(cloudId, out var prevCloud))
                {
                    if (prevCloud.HasUnsyncedCloudEdit)
                    {
                        // Preserve user's unsynced local draft until they explicitly sync or delete it.
                        mergedCloudItems.Add(prevCloud);
                    }
                    else
                    {
                        mergedCloudItems.Add(prevCloud with
                        {
                            Word = cleanWord,
                            Sentence = sentence,
                            Translation = translation,
                            Source = source,
                            CloudId = cloudId,
                            Origin = "cloud",
                            AccountKey = cleanAccount,
                            Tags = tags.Count > 0 ? tags : null
                        });
                    }
                    continue;
                }

                var sentenceKey = NormalizeSentenceKey(sentence);
                if (existingLocalBySentence.TryGetValue(sentenceKey, out var prevLocal) && promotedLocalIds.Add(prevLocal.Id))
                {
                    mergedCloudItems.Add(prevLocal with
                    {
                        Word = cleanWord,
                        Sentence = sentence,
                        Translation = translation.Length > 0 ? translation : prevLocal.Translation,
                        Source = source.Length > 0 ? source : prevLocal.Source,
                        CloudId = cloudId,
                        Origin = "cloud",
                        AccountKey = cleanAccount,
                        Tags = tags.Count > 0 ? tags : prevLocal.Tags
                    });
                    continue;
                }

                mergedCloudItems.Add(new ContextSentenceItem(
                    "cloud:" + cloudId,
                    cleanWord,
                    sentence,
                    translation,
                    source,
                    null,
                    DateTimeOffset.UtcNow,
                    0,
                    null,
                    CloudId: cloudId,
                    Origin: "cloud",
                    AccountKey: cleanAccount,
                    Tags: tags.Count > 0 ? tags : null));
            }

            // Rebuild list: keep other words, keep other accounts' cloud items, keep non-promoted local items,
            // keep unsynced local edits whose cloud phrase was removed (demoted to local so user doesn't lose edits),
            // and insert refreshed cloud items at the top of this word.
            var remaining = new List<ContextSentenceItem>(_items.Count);
            foreach (var item in _items)
            {
                if (!item.Word.Equals(cleanWord, StringComparison.OrdinalIgnoreCase))
                {
                    remaining.Add(item);
                    continue;
                }
                if (promotedLocalIds.Contains(item.Id)) continue;
                if (item.AccountKey == cleanAccount && !string.IsNullOrWhiteSpace(item.CloudId))
                {
                    if (incomingCloudIds.Contains(item.CloudId)) continue; // replaced by mergedCloudItems
                    if (item.HasUnsyncedCloudEdit)
                    {
                        // Cloud copy was deleted elsewhere; keep user's draft locally.
                        remaining.Add(item with { CloudId = null, Origin = "local", AccountKey = null });
                    }
                    continue;
                }
                remaining.Add(item);
            }

            // Preserve relative order: place newly merged cloud items first for current word.
            var next = new List<ContextSentenceItem>(mergedCloudItems.Count + remaining.Count);
            next.AddRange(mergedCloudItems);
            next.AddRange(remaining);

            var changed = next.Count != _items.Count || !next.SequenceEqual(_items, ContextItemStructuralComparer.Instance);
            if (changed)
            {
                _items = next;
                TrimCapacityLocked();
                PersistLocked();
            }

            return _items
                .Where(item => IsVisibleTo(item, cleanAccount) && item.Word.Equals(cleanWord, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_gate)
        {
            EnsureLoaded();
            var removed = _items.RemoveAll(item => item.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) PersistLocked();
            return removed;
        }
    }

    public ContextSentenceItem? RecordReview(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            EnsureLoaded();
            var idx = _items.FindIndex(item => item.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return null;
            var updated = _items[idx] with
            {
                ReviewCount = _items[idx].ReviewCount + 1,
                LastReviewedAt = DateTimeOffset.UtcNow
            };
            _items[idx] = updated;
            PersistLocked();
            return updated;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_path)) return;
            var raw = JsonSerializer.Deserialize<List<ContextSentenceItem>>(File.ReadAllText(_path), JsonOptions);
            if (raw == null) return;
            _items = raw.Where(item => item != null
                && !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.Word)
                && !string.IsNullOrWhiteSpace(item.Sentence))
                .Select(item => string.IsNullOrWhiteSpace(item.Origin) ? item with { Origin = "local" } : item)
                .Take(MaxItems)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _items = [];
        }
    }

    private void TrimCapacityLocked()
    {
        if (_items.Count > MaxItems)
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
    }

    private void PersistLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsVisibleTo(ContextSentenceItem item, string? accountKey) =>
        string.IsNullOrWhiteSpace(item.AccountKey)
        || (!string.IsNullOrWhiteSpace(accountKey) && string.Equals(item.AccountKey, accountKey, StringComparison.Ordinal));

    private static string CleanWord(string word)
    {
        var clean = (word ?? "").Trim();
        if (clean.Length is < 1 or > 100) throw new ArgumentException("请输入有效单词。");
        return clean;
    }

    private static string CleanSentence(string sentence)
    {
        var clean = Regex.Replace((sentence ?? "").Trim(), @"\s+", " ");
        if (clean.Length is < 1 or > 2000) throw new ArgumentException("例句内容需为 1–2000 个字符。");
        return clean;
    }

    private static string CleanSentenceOrFallback(string sentence)
    {
        var clean = Regex.Replace((sentence ?? "").Trim(), @"\s+", " ");
        return clean.Length <= 2000 ? clean : clean[..1997] + "…";
    }

    private static string CleanTranslation(string? translation)
    {
        var clean = Regex.Replace((translation ?? "").Trim(), @"\s+", " ");
        return clean.Length <= 2000 ? clean : clean[..1997] + "…";
    }

    private static string CleanSource(string? source, string fallback)
    {
        var clean = string.IsNullOrWhiteSpace(source) ? fallback : source.Trim();
        return clean.Length <= 100 ? clean : clean[..100];
    }

    private static IReadOnlyList<string> CleanTags(IReadOnlyList<string>? tags) =>
        tags == null
            ? []
            : tags.Select(t => (t ?? "").Trim())
                .Where(t => t.Length > 0)
                .Select(t => t.Length > 30 ? t[..30] : t)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray();

    private static string NormalizeSentenceKey(string sentence) =>
        Regex.Replace(sentence.Trim(), @"\s+", " ");

    private sealed class ContextItemStructuralComparer : IEqualityComparer<ContextSentenceItem>
    {
        public static readonly ContextItemStructuralComparer Instance = new();
        public bool Equals(ContextSentenceItem? x, ContextSentenceItem? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.Id == y.Id
                && x.Word == y.Word
                && x.Sentence == y.Sentence
                && x.Translation == y.Translation
                && x.Source == y.Source
                && x.MatchedForm == y.MatchedForm
                && x.ReviewCount == y.ReviewCount
                && x.LastReviewedAt == y.LastReviewedAt
                && x.CloudId == y.CloudId
                && x.Origin == y.Origin
                && x.AccountKey == y.AccountKey
                && ((x.Tags == null && y.Tags == null) || (x.Tags != null && y.Tags != null && x.Tags.SequenceEqual(y.Tags)));
        }
        public int GetHashCode(ContextSentenceItem obj) => HashCode.Combine(obj.Id, obj.Sentence, obj.CloudId, obj.Origin);
    }
}
