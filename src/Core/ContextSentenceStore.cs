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
    DateTimeOffset? LastReviewedAt = null);

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
}

public sealed class ContextSentenceStore
{
    public const int MaxItems = 500;
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

    public int Count
    {
        get { lock (_gate) { EnsureLoaded(); return _items.Count; } }
    }

    public IReadOnlyList<ContextSentenceItem> List(string? word = null)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(word))
                return _items.ToArray();
            var target = word.Trim();
            return _items.Where(item => item.Word.Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    public ContextSentenceItem? FindLatestForWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        lock (_gate)
        {
            EnsureLoaded();
            var target = word.Trim();
            return _items.FirstOrDefault(item => item.Word.Equals(target, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool Contains(string word, string sentence)
    {
        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(sentence)) return false;
        var w = word.Trim();
        var s = NormalizeSentenceKey(sentence);
        lock (_gate)
        {
            EnsureLoaded();
            return _items.Any(item => item.Word.Equals(w, StringComparison.OrdinalIgnoreCase)
                && NormalizeSentenceKey(item.Sentence).Equals(s, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ContextSentenceItem Save(string word, string sentence, string translation = "", string source = "查词保存", string? matchedForm = null)
    {
        var cleanWord = word.Trim();
        var cleanSentence = Regex.Replace(sentence.Trim(), @"\s+", " ");
        if (cleanWord.Length is < 1 or > 100 || cleanSentence.Length is < 3 or > 600)
            throw new ArgumentException("单词或原句内容无效。");
        var cleanTranslation = Regex.Replace((translation ?? "").Trim(), @"\s+", " ");
        if (cleanTranslation.Length > 500) cleanTranslation = cleanTranslation[..497] + "…";
        var cleanSource = string.IsNullOrWhiteSpace(source) ? "查词保存" : source.Trim();
        var cleanForm = string.IsNullOrWhiteSpace(matchedForm) ? null : matchedForm.Trim();

        lock (_gate)
        {
            EnsureLoaded();
            var key = NormalizeSentenceKey(cleanSentence);
            var existingIdx = _items.FindIndex(item => item.Word.Equals(cleanWord, StringComparison.OrdinalIgnoreCase)
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
                    null);
                _items.Insert(0, saved);
                if (_items.Count > MaxItems)
                    _items.RemoveRange(MaxItems, _items.Count - MaxItems);
            }
            PersistLocked();
            return saved;
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
                .Take(MaxItems)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _items = [];
        }
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

    private static string NormalizeSentenceKey(string sentence) =>
        Regex.Replace(sentence.Trim(), @"\s+", " ");
}
