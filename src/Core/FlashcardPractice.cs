namespace MemoLex.Core;

public enum FlashcardAnswer { Known, Vague, Forgotten }

public sealed record FlashcardProgressSnapshot(
    string[] Words,
    int Cursor,
    int GroupNumber,
    int GroupWordCount,
    string[] Queue,
    DateTimeOffset? UpdatedAt = null);

public sealed class FlashcardPractice
{
    public const int GroupSize = 10;
    private string[] _words;
    private int _cursor;
    private bool _restored;
    private readonly List<string> _queue = [];

    public FlashcardPractice(IEnumerable<string> words, FlashcardProgressSnapshot? saved = null)
    {
        _words = Normalize(words);
        if (saved != null && TryRestore(saved))
        {
            _restored = true;
            return;
        }
        _restored = false;
        _cursor = 0;
        GroupNumber = 0;
        GroupWordCount = 0;
        _queue.Clear();
        if (_words.Length > 0) StartNextGroup();
    }

    public int TotalWordCount => _words.Length;
    public int GroupNumber { get; private set; }
    public int GroupCount => GroupNumber + (_words.Length - _cursor + GroupSize - 1) / GroupSize;
    public int GroupWordCount { get; private set; }
    public int Remaining => _queue.Count;
    public int MasteredCount => Math.Clamp(_cursor - _queue.Count, 0, _words.Length);
    public string? Current => _queue.Count == 0 ? null : _queue[0];
    public bool HasNextGroup => _cursor < _words.Length;
    public bool HasProgress =>
        _restored
        || MasteredCount > 0
        || GroupNumber > 1
        || (_words.Length > 0 && !_queue.SequenceEqual(_words.Take(GroupWordCount), StringComparer.OrdinalIgnoreCase));

    public bool StartNextGroup()
    {
        if (Remaining != 0 || !HasNextGroup) return false;
        var batch = _words.Skip(_cursor).Take(GroupSize).ToArray();
        _queue.AddRange(batch);
        _cursor += batch.Length;
        GroupNumber++;
        GroupWordCount = batch.Length;
        return true;
    }

    public void Answer(FlashcardAnswer answer)
    {
        if (Current is not { } word) return;
        _queue.RemoveAt(0);
        switch (answer)
        {
            case FlashcardAnswer.Vague: _queue.Insert(Math.Min(3, Remaining), word); break;
            case FlashcardAnswer.Forgotten: _queue.Add(word); break;
        }
    }

    public void Reset(IEnumerable<string>? words = null)
    {
        if (words != null) _words = Normalize(words);
        _restored = false;
        _cursor = 0;
        GroupNumber = 0;
        GroupWordCount = 0;
        _queue.Clear();
        if (_words.Length > 0) StartNextGroup();
    }

    public FlashcardProgressSnapshot ExportSnapshot() =>
        new(_words.ToArray(), _cursor, GroupNumber, GroupWordCount, _queue.ToArray(), DateTimeOffset.UtcNow);

    private bool TryRestore(FlashcardProgressSnapshot saved)
    {
        if (_words.Length == 0 || saved.Words is not { Length: > 0 } || saved.Queue == null)
            return false;

        var incomingByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in _words) incomingByKey.TryAdd(word, word);

        var savedWords = Normalize(saved.Words);
        if (savedWords.Length == 0) return false;

        var savedCursor = Math.Clamp(saved.Cursor, 0, savedWords.Length);
        var activeQueue = new List<string>();
        var queueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in saved.Queue)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var key = raw.Trim();
            if (incomingByKey.TryGetValue(key, out var canonical) && queueKeys.Add(canonical))
                activeQueue.Add(canonical);
        }

        var mastered = new List<string>();
        var placed = new HashSet<string>(queueKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var word in savedWords.Take(savedCursor))
        {
            if (incomingByKey.TryGetValue(word, out var canonical) && placed.Add(canonical))
                mastered.Add(canonical);
        }

        var tail = new List<string>();
        foreach (var word in savedWords.Skip(savedCursor))
        {
            if (incomingByKey.TryGetValue(word, out var canonical) && placed.Add(canonical))
                tail.Add(canonical);
        }
        foreach (var word in _words)
        {
            if (placed.Add(word))
                tail.Add(word);
        }

        if (mastered.Count == 0 && activeQueue.Count == 0)
            return false;

        _words = [.. mastered, .. activeQueue, .. tail];
        _cursor = mastered.Count + activeQueue.Count;
        _queue.Clear();
        _queue.AddRange(activeQueue);
        GroupNumber = Math.Max(1, saved.GroupNumber);
        GroupWordCount = Math.Clamp(Math.Max(activeQueue.Count, saved.GroupWordCount), activeQueue.Count, Math.Max(activeQueue.Count, GroupSize));
        return true;
    }

    private static string[] Normalize(IEnumerable<string> words) =>
        words.Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
