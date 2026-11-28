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
    DateTimeOffset UpdatedAt = default);

public sealed record MistakeRightResult(
    bool WasInBook,
    int RemainingWeight,
    bool Removed);

public sealed class MistakeBookStore
{
    public const int WrongDelta = 3;
    public const int RightDelta = 1;
    public const int MaxWeight = 99;
    public const int MaxWords = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, MistakeBookItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

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

    public MistakeBookItem? Get(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        lock (_gate)
        {
            EnsureLoaded();
            return _items.TryGetValue(word.Trim(), out var item) ? item : null;
        }
    }

    public IReadOnlyList<MistakeBookItem> List()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _items.Values
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
    public IReadOnlyList<string> PickWeightedRandom(IReadOnlyList<string>? filterWords = null, int maxCount = 0)
    {
        lock (_gate)
        {
            EnsureLoaded();
            IEnumerable<MistakeBookItem> pool = _items.Values;
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
                    return (item.Word, Key: key);
                })
                .OrderByDescending(x => x.Key)
                .Select(x => x.Word);

            return maxCount > 0 ? ordered.Take(maxCount).ToArray() : ordered.ToArray();
        }
    }

    public MistakeBookItem RecordWrong(
        string word,
        string reason = "拼写错误",
        string? wrongInput = null,
        string? contextSentence = null,
        string? contextTranslation = null)
    {
        var cleanWord = (word ?? "").Trim();
        if (cleanWord.Length is < 1 or > 100)
            throw new ArgumentException("请输入有效单词。", nameof(word));
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? "拼写错误" : reason.Trim();
        var cleanWrong = string.IsNullOrWhiteSpace(wrongInput) ? null : wrongInput.Trim();
        if (cleanWrong != null && cleanWrong.Length > 100) cleanWrong = cleanWrong[..100];
        var cleanSentence = string.IsNullOrWhiteSpace(contextSentence) ? null : contextSentence.Trim();
        if (cleanSentence != null && cleanSentence.Length > 600) cleanSentence = cleanSentence[..597] + "…";
        var cleanTrans = string.IsNullOrWhiteSpace(contextTranslation) ? null : contextTranslation.Trim();
        if (cleanTrans != null && cleanTrans.Length > 400) cleanTrans = cleanTrans[..397] + "…";

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            EnsureLoaded();
            MistakeBookItem updated;
            if (_items.TryGetValue(cleanWord, out var existing))
            {
                updated = existing with
                {
                    Weight = Math.Min(MaxWeight, Math.Max(0, existing.Weight) + WrongDelta),
                    WrongCount = existing.WrongCount + 1,
                    LastReason = cleanReason,
                    LastWrongInput = cleanWrong ?? existing.LastWrongInput,
                    ContextSentence = cleanSentence ?? existing.ContextSentence,
                    ContextTranslation = cleanTrans ?? existing.ContextTranslation,
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
                    UpdatedAt: now);
            }
            _items[cleanWord] = updated;
            TrimLocked();
            PersistLocked();
            return updated;
        }
    }

    public MistakeRightResult RecordRight(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return new MistakeRightResult(false, 0, false);
        var cleanWord = word.Trim();
        lock (_gate)
        {
            EnsureLoaded();
            if (!_items.TryGetValue(cleanWord, out var existing))
                return new MistakeRightResult(false, 0, false);

            var nextWeight = existing.Weight - RightDelta;
            if (nextWeight <= 0)
            {
                _items.Remove(cleanWord);
                PersistLocked();
                return new MistakeRightResult(true, 0, true);
            }

            _items[cleanWord] = existing with
            {
                Weight = nextWeight,
                RightCount = existing.RightCount + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            PersistLocked();
            return new MistakeRightResult(true, nextWeight, false);
        }
    }

    public bool Remove(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;
        lock (_gate)
        {
            EnsureLoaded();
            var removed = _items.Remove(word.Trim());
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
            .ToDictionary(i => i.Word, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_path)) return;
            var raw = JsonSerializer.Deserialize<List<MistakeBookItem>>(File.ReadAllText(_path), JsonOptions);
            if (raw == null) return;
            _items = new Dictionary<string, MistakeBookItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in raw)
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.Word) && item.Weight > 0)
                    _items[item.Word.Trim()] = item;
            }
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
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items.Values.ToArray(), JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
