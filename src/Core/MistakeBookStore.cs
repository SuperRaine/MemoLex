using System.IO;
using System.Text.Json;

namespace MemoLex.Core;

public sealed record MistakeBookItem(
    string Word,
    int Weight,
    int WrongCount,
    int RightCount,
    string LastReason = "拼写错误",
    string? LastWrongInput = null,
    string? ContextSentence = null,
    string? ContextTranslation = null,
    DateTimeOffset FirstWrongAt = default,
    DateTimeOffset UpdatedAt = default,
    string? Category = null,
    string? ContextItemId = null);

public sealed record MistakeRightResult(
    bool WasInBook,
    int RemainingWeight,
    bool Removed);

public sealed class MistakeBookStore
{
    public const string CategorySpelling = "spelling";
    public const string CategorySentence = "sentence";

    public const int WrongDelta = 3;
    public const int RightDelta = 1;
    public const int MaxWeight = 99;
    public const int MaxWords = 1000;

    public static string LevelLabel(int weight) => weight >= 6 ? "重点易错" : weight >= 3 ? "待巩固" : "快掌握";

    /// <summary>
    /// Resolves sentence-quiz mistake entries into playable ContextSentenceItems for 4-choice review,
    /// preserving the caller's ordering and falling back to stored mistake sentence text if the original
    /// saved sentence was edited or removed.
    /// </summary>
    public static IReadOnlyList<ContextSentenceItem> BuildSentenceDeck(
        IReadOnlyList<MistakeBookItem> mistakes,
        IReadOnlyList<ContextSentenceItem> savedSentences)
    {
        if (mistakes.Count == 0) return [];
        var byId = new Dictionary<string, ContextSentenceItem>(StringComparer.OrdinalIgnoreCase);
        var byWord = new Dictionary<string, List<ContextSentenceItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in savedSentences)
        {
            if (!string.IsNullOrWhiteSpace(s.Id))
                byId.TryAdd(s.Id, s);
            if (!string.IsNullOrWhiteSpace(s.Word))
            {
                if (!byWord.TryGetValue(s.Word, out var list))
                {
                    list = [];
                    byWord[s.Word] = list;
                }
                list.Add(s);
            }
        }

        var deck = new List<ContextSentenceItem>(mistakes.Count);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in mistakes)
        {
            if (string.IsNullOrWhiteSpace(item.Word)) continue;
            ContextSentenceItem? matched = null;

            if (!string.IsNullOrWhiteSpace(item.ContextItemId) && byId.TryGetValue(item.ContextItemId, out var byIdHit))
                matched = byIdHit;

            if (matched == null && !string.IsNullOrWhiteSpace(item.ContextSentence) && byWord.TryGetValue(item.Word, out var wordHits))
            {
                matched = wordHits.FirstOrDefault(s =>
                    string.Equals(s.Sentence.Trim(), item.ContextSentence.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (matched == null && !string.IsNullOrWhiteSpace(item.ContextSentence))
            {
                matched = new ContextSentenceItem(
                    Id: !string.IsNullOrWhiteSpace(item.ContextItemId) ? item.ContextItemId : $"mistake:{item.Word.Trim().ToLowerInvariant()}",
                    Word: item.Word.Trim(),
                    Sentence: item.ContextSentence.Trim(),
                    Translation: item.ContextTranslation?.Trim() ?? "",
                    Source: "例句错题");
            }

            if (matched == null && byWord.TryGetValue(item.Word, out var fallbackList) && fallbackList.Count > 0)
                matched = fallbackList[0];

            if (matched == null || string.IsNullOrWhiteSpace(matched.Sentence)) continue;
            var pairKey = $"{matched.Word.Trim()}|{matched.Sentence.Trim()}";
            if (!usedIds.Add(matched.Id) || !usedPairs.Add(pairKey)) continue;
            deck.Add(matched);
        }

        return deck;
    }

    public static string? NormalizeCategory(string? categoryOrReason)
    {
        if (string.IsNullOrWhiteSpace(categoryOrReason)) return null;
        var s = categoryOrReason.Trim();
        if (s.Equals(CategorySpelling, StringComparison.OrdinalIgnoreCase) || s.Contains("拼写"))
            return CategorySpelling;
        if (s.Equals(CategorySentence, StringComparison.OrdinalIgnoreCase) || s.Contains("例句"))
            return CategorySentence;
        return null;
    }

    private static string KeyOf(string? category, string word) => $"{NormalizeCategory(category) ?? CategorySpelling}:{word.Trim()}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, MistakeBookItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastWrongAt;
    private DateTimeOffset _lastViewedAt;
    private bool _loaded;

    private sealed record MistakeBookEnvelope(
        DateTimeOffset LastWrongAt,
        DateTimeOffset LastViewedAt,
        MistakeBookItem[] Items);

    public MistakeBookStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoLex", "mistake-book.json");
    }

    public int Count
    {
        get { lock (_gate) { EnsureLoaded(); return _items.Count; } }
    }

    public bool HasUnread
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _items.Count > 0 && _lastWrongAt > _lastViewedAt;
            }
        }
    }

    public void MarkViewed()
    {
        lock (_gate)
        {
            EnsureLoaded();
            var now = DateTimeOffset.UtcNow;
            if (now <= _lastWrongAt) now = _lastWrongAt.AddTicks(1);
            if (_lastViewedAt >= _lastWrongAt && _lastViewedAt != default) return;
            _lastViewedAt = now;
            if (_items.Count > 0 || File.Exists(_path))
                PersistLocked();
        }
    }

    public int TotalWeight
    {
        get { lock (_gate) { EnsureLoaded(); return _items.Values.Sum(i => i.Weight); } }
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

    public MistakeBookItem? Get(string word, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        var clean = word.Trim();
        var cat = NormalizeCategory(category);
        lock (_gate)
        {
            EnsureLoaded();
            if (cat != null)
                return _items.TryGetValue(KeyOf(cat, clean), out var exact) ? exact : null;
            if (_items.TryGetValue(KeyOf(CategorySpelling, clean), out var sp)) return sp;
            if (_items.TryGetValue(KeyOf(CategorySentence, clean), out var se)) return se;
            return null;
        }
    }

    public IReadOnlyList<MistakeBookItem> List(string? category = null)
    {
        var cat = NormalizeCategory(category);
        lock (_gate)
        {
            EnsureLoaded();
            IEnumerable<MistakeBookItem> pool = _items.Values;
            if (cat != null) pool = pool.Where(i => string.Equals(i.Category, cat, StringComparison.OrdinalIgnoreCase));
            return pool
                .OrderByDescending(i => i.Weight)
                .ThenByDescending(i => i.UpdatedAt)
                .ThenBy(i => i.Word, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>
    /// Returns words ordered by weighted random sampling (Efraimidis-Spirakis):
    /// higher weight words surface earlier with higher probability while remaining randomized.
    /// </summary>
    public IReadOnlyList<string> PickWeightedRandom(IReadOnlyList<string>? filterWords = null, int maxCount = 0, string? category = null)
    {
        var items = PickWeightedRandomItems(category, filterWords, maxCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = new List<string>(items.Count);
        foreach (var it in items)
            if (seen.Add(it.Word)) words.Add(it.Word);
        return words;
    }

    public IReadOnlyList<MistakeBookItem> PickWeightedRandomItems(string? category = null, IReadOnlyList<string>? filterWords = null, int maxCount = 0)
    {
        var cat = NormalizeCategory(category);
        lock (_gate)
        {
            EnsureLoaded();
            IEnumerable<MistakeBookItem> pool = _items.Values;
            if (cat != null)
                pool = pool.Where(i => string.Equals(i.Category, cat, StringComparison.OrdinalIgnoreCase));
            if (filterWords is { Count: > 0 })
            {
                var set = new HashSet<string>(filterWords.Select(w => w.Trim()), StringComparer.OrdinalIgnoreCase);
                pool = pool.Where(i => set.Contains(i.Word));
            }
            var list = pool.ToList();
            if (list.Count == 0) return [];

            // Efraimidis-Spirakis key: u^(1 / weight), larger key = higher priority.
            var ordered = list
                .Select(item =>
                {
                    var u = Math.Max(1e-9, Random.Shared.NextDouble());
                    var w = Math.Max(1, item.Weight);
                    var key = Math.Pow(u, 1.0 / w);
                    return (Item: item, Key: key);
                })
                .OrderByDescending(x => x.Key)
                .Select(x => x.Item);

            return maxCount > 0 ? ordered.Take(maxCount).ToArray() : ordered.ToArray();
        }
    }

    public MistakeBookItem? RecordWrong(
        string word,
        string reason = "拼写错误",
        string? wrongInput = null,
        string? contextSentence = null,
        string? contextTranslation = null,
        string? contextItemId = null)
    {
        var cleanWord = (word ?? "").Trim();
        if (cleanWord.Length is < 1 or > 100)
            throw new ArgumentException("请输入有效单词。", nameof(word));

        var category = NormalizeCategory(reason);
        // Mistake book strictly collects only spelling mistakes and sentence-quiz mistakes.
        if (category == null) return null;

        var cleanReason = category == CategorySentence ? "例句填空" : "拼写错误";
        var cleanWrong = string.IsNullOrWhiteSpace(wrongInput) ? null : wrongInput.Trim();
        if (cleanWrong != null && cleanWrong.Length > 100) cleanWrong = cleanWrong[..100];
        var cleanSentence = string.IsNullOrWhiteSpace(contextSentence) ? null : contextSentence.Trim();
        if (cleanSentence != null && cleanSentence.Length > 600) cleanSentence = cleanSentence[..597] + "…";
        var cleanTrans = string.IsNullOrWhiteSpace(contextTranslation) ? null : contextTranslation.Trim();
        if (cleanTrans != null && cleanTrans.Length > 400) cleanTrans = cleanTrans[..397] + "…";
        var cleanItemId = string.IsNullOrWhiteSpace(contextItemId) ? null : contextItemId.Trim();

        var key = KeyOf(category, cleanWord);
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            EnsureLoaded();
            MistakeBookItem updated;
            if (_items.TryGetValue(key, out var existing))
            {
                updated = existing with
                {
                    Category = category,
                    Weight = Math.Min(MaxWeight, Math.Max(0, existing.Weight) + WrongDelta),
                    WrongCount = existing.WrongCount + 1,
                    LastReason = cleanReason,
                    LastWrongInput = cleanWrong ?? existing.LastWrongInput,
                    ContextSentence = cleanSentence ?? existing.ContextSentence,
                    ContextTranslation = cleanTrans ?? existing.ContextTranslation,
                    ContextItemId = cleanItemId ?? existing.ContextItemId,
                    UpdatedAt = now
                };
            }
            else
            {
                updated = new MistakeBookItem(
                    cleanWord,
                    Weight: WrongDelta,
                    WrongCount: 1,
                    RightCount: 0,
                    LastReason: cleanReason,
                    LastWrongInput: cleanWrong,
                    ContextSentence: cleanSentence,
                    ContextTranslation: cleanTrans,
                    FirstWrongAt: now,
                    UpdatedAt: now,
                    Category: category,
                    ContextItemId: cleanItemId);
            }
            _items[key] = updated;
            _lastWrongAt = now >= _lastWrongAt ? now : _lastWrongAt.AddTicks(1);
            if (_lastWrongAt <= _lastViewedAt) _lastWrongAt = _lastViewedAt.AddTicks(1);
            TrimLocked();
            PersistLocked();
            return updated;
        }
    }

    public MistakeRightResult RecordRight(string word, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(word))
            return new MistakeRightResult(false, 0, false);
        var cleanWord = word.Trim();
        var cat = NormalizeCategory(category) ?? CategorySpelling;
        var key = KeyOf(cat, cleanWord);
        lock (_gate)
        {
            EnsureLoaded();
            if (!_items.TryGetValue(key, out var existing))
                return new MistakeRightResult(false, 0, false);

            var nextWeight = existing.Weight - RightDelta;
            if (nextWeight <= 0)
            {
                _items.Remove(key);
                PersistLocked();
                return new MistakeRightResult(true, 0, true);
            }

            _items[key] = existing with
            {
                Weight = nextWeight,
                RightCount = existing.RightCount + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            PersistLocked();
            return new MistakeRightResult(true, nextWeight, false);
        }
    }

    public bool Remove(string word, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;
        var clean = word.Trim();
        var cat = NormalizeCategory(category);
        lock (_gate)
        {
            EnsureLoaded();
            bool removed;
            if (cat != null)
            {
                removed = _items.Remove(KeyOf(cat, clean));
            }
            else
            {
                var r1 = _items.Remove(KeyOf(CategorySpelling, clean));
                var r2 = _items.Remove(KeyOf(CategorySentence, clean));
                removed = r1 || r2;
            }
            if (removed) PersistLocked();
            return removed;
        }
    }

    public int Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            var removed = _items.Count;
            _items.Clear();
            _lastWrongAt = default;
            _lastViewedAt = DateTimeOffset.UtcNow;
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return removed;
        }
    }

    private void TrimLocked()
    {
        if (_items.Count <= MaxWords) return;
        _items = _items.Values
            .OrderByDescending(i => i.Weight)
            .ThenByDescending(i => i.UpdatedAt)
            .Take(MaxWords)
            .ToDictionary(i => KeyOf(i.Category, i.Word), StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_path)) return;
            var text = File.ReadAllText(_path).TrimStart();
            if (text.Length == 0) return;

            List<MistakeBookItem>? raw = null;
            if (text[0] == '{')
            {
                var env = JsonSerializer.Deserialize<MistakeBookEnvelope>(text, JsonOptions);
                if (env != null)
                {
                    _lastWrongAt = env.LastWrongAt;
                    _lastViewedAt = env.LastViewedAt;
                    raw = env.Items?.ToList();
                }
            }
            else
            {
                raw = JsonSerializer.Deserialize<List<MistakeBookItem>>(text, JsonOptions);
            }

            if (raw == null) return;
            _items = new Dictionary<string, MistakeBookItem>(StringComparer.OrdinalIgnoreCase);
            var migrated = false;
            foreach (var item in raw)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Word) || item.Weight <= 0)
                {
                    migrated = true;
                    continue;
                }
                var cat = NormalizeCategory(item.Category) ?? NormalizeCategory(item.LastReason);
                // Drop legacy flashcard/morpheme entries so the Mistake Book only retains spelling & sentence quiz items.
                if (cat == null)
                {
                    migrated = true;
                    continue;
                }
                var normalizedReason = cat == CategorySentence ? "例句填空" : "拼写错误";
                var normalized = item with
                {
                    Word = item.Word.Trim(),
                    Category = cat,
                    LastReason = normalizedReason
                };
                if (!string.Equals(item.Category, cat, StringComparison.Ordinal) || item.LastReason != normalizedReason)
                    migrated = true;
                _items[KeyOf(cat, normalized.Word)] = normalized;
            }
            if (_items.Count > 0 && _lastWrongAt == default)
            {
                var maxUpdated = _items.Values.Max(i => i.UpdatedAt);
                _lastWrongAt = maxUpdated != default ? maxUpdated : DateTimeOffset.UtcNow;
            }
            if (migrated) PersistLocked();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _items = new Dictionary<string, MistakeBookItem>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void PersistLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            var envelope = new MistakeBookEnvelope(_lastWrongAt, _lastViewedAt, _items.Values.ToArray());
            File.WriteAllText(tmp, JsonSerializer.Serialize(envelope, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
