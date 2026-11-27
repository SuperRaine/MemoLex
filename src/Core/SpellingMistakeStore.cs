using System.IO;
using System.Text.Json;

namespace MemoLex.Core;

public sealed record SpellingMistakeAnalysis(
    string PatternKey,
    string Kind,
    string ContextMask,
    string Question,
    string CorrectOption,
    string WrongOption,
    string Explanation);

public sealed record SpellingMistakeRecord(
    string Word,
    string PatternKey,
    string Kind,
    string LastWrongInput,
    string ContextMask,
    string Question,
    string CorrectOption,
    string WrongOption,
    string Explanation,
    int MistakeCount,
    int CleanStreak,
    DateTimeOffset UpdatedAt);

public sealed record SpellingMicroDrill(
    string Word,
    string ContextMask,
    string Question,
    string CorrectOption,
    string WrongOption,
    string Explanation,
    string LastWrongInput,
    int MistakeCount);

public static class SpellingMistakeAnalyzer
{
    public static SpellingMistakeAnalysis? Analyze(string expectedRaw, string typedRaw)
    {
        var expected = expectedRaw.Trim().ToLowerInvariant();
        var typed = typedRaw.Trim().ToLowerInvariant();
        if (expected.Length < 4 || typed.Length < 3 || expected == typed) return null;
        if (!expected.All(char.IsAsciiLetter) || !typed.All(char.IsAsciiLetter)) return null;

        // Avoid recording random unrelated typing or barely-started prefixes (e.g. typing "ne" for "necessary").
        if (typed.Length < expected.Length - 2 && expected.StartsWith(typed, StringComparison.Ordinal))
            return null;
        if (Math.Abs(expected.Length - typed.Length) > 3) return null;

        var prefix = 0;
        var minLen = Math.Min(expected.Length, typed.Length);
        while (prefix < minLen && expected[prefix] == typed[prefix]) prefix++;

        var suffix = 0;
        while (suffix < (expected.Length - prefix)
            && suffix < (typed.Length - prefix)
            && expected[expected.Length - 1 - suffix] == typed[typed.Length - 1 - suffix])
        {
            suffix++;
        }

        var expDiff = expected.Substring(prefix, expected.Length - prefix - suffix);
        var typDiff = typed.Substring(prefix, typed.Length - prefix - suffix);
        if (expDiff.Length == 0 && typDiff.Length == 0) return null;

        // Keep edits localized enough to produce a sharp, helpful micro-question.
        if (expDiff.Length > 3 || typDiff.Length > 3) return null;
        var unchanged = prefix + suffix;
        if (unchanged < Math.Max(2, expected.Length / 2)) return null;

        // 1. Adjacent transposition (e.g. receive -> recieve, believe -> beleive)
        if (expDiff.Length == 2 && typDiff.Length == 2 && expDiff[0] == typDiff[1] && expDiff[1] == typDiff[0])
        {
            var left = expected[..prefix];
            var right = expected[(expected.Length - suffix)..];
            var mask = $"{left} ___ {right}".Trim();
            return new SpellingMistakeAnalysis(
                $"transpose:{prefix}:{expDiff}:{typDiff}",
                "Transposition",
                mask,
                $"这里的字母顺序是 {expDiff} 还是 {typDiff}？",
                expDiff,
                typDiff,
                $"正确顺序是 {expDiff}：{left}·{expDiff}·{right}");
        }

        // 2. Extra doubled letter (e.g. necessary -> neccessary: expDiff="", typDiff="c" next to 'c')
        if (expDiff.Length == 0 && typDiff.Length == 1)
        {
            var ch = typDiff[0];
            var prevMatch = prefix > 0 && expected[prefix - 1] == ch;
            var nextMatch = suffix > 0 && expected[expected.Length - suffix] == ch;
            if (prevMatch || nextMatch)
            {
                var runStart = prevMatch ? prefix - 1 : prefix;
                while (runStart > 0 && expected[runStart - 1] == ch) runStart--;
                var runEnd = runStart;
                while (runEnd < expected.Length && expected[runEnd] == ch) runEnd++;
                var count = runEnd - runStart;
                var correctRun = new string(ch, count);
                var wrongRun = new string(ch, count + 1);
                var left = expected[..runStart];
                var right = expected[runEnd..];
                var mask = $"{left} ___ {right}".Trim();
                return new SpellingMistakeAnalysis(
                    $"double:{runStart}:{correctRun}:{wrongRun}",
                    "DoubleLetter",
                    mask,
                    $"这里是一个 {ch} 还是两个 {ch}？",
                    correctRun,
                    wrongRun,
                    $"正确是 {count} 个 {ch}：{left}·{correctRun}·{right}");
            }
        }

        // 3. Missing doubled letter (e.g. accommodate -> acommodate: expDiff="c", typDiff="" next to 'c')
        if (expDiff.Length == 1 && typDiff.Length == 0)
        {
            var ch = expDiff[0];
            var prevMatch = prefix > 0 && expected[prefix - 1] == ch;
            var nextMatch = suffix > 0 && expected[expected.Length - suffix] == ch;
            if (prevMatch || nextMatch)
            {
                var runStart = prevMatch ? prefix - 1 : prefix;
                while (runStart > 0 && expected[runStart - 1] == ch) runStart--;
                var runEnd = runStart;
                while (runEnd < expected.Length && expected[runEnd] == ch) runEnd++;
                var count = runEnd - runStart;
                var correctRun = new string(ch, count);
                var wrongRun = new string(ch, Math.Max(1, count - 1));
                var left = expected[..runStart];
                var right = expected[runEnd..];
                var mask = $"{left} ___ {right}".Trim();
                return new SpellingMistakeAnalysis(
                    $"double:{runStart}:{correctRun}:{wrongRun}",
                    "DoubleLetter",
                    mask,
                    $"这里是一个 {ch} 还是两个 {ch}？",
                    correctRun,
                    wrongRun,
                    $"正确是 {count} 个 {ch}：{left}·{correctRun}·{right}");
            }
        }

        // 4. Local substitution (e.g. separate -> seperate, independent -> independant)
        if (expDiff.Length >= 1 && typDiff.Length >= 1)
        {
            var left = expected[..prefix];
            var right = expected[(expected.Length - suffix)..];
            var mask = $"{left} ___ {right}".Trim();
            return new SpellingMistakeAnalysis(
                $"sub:{prefix}:{expDiff}:{typDiff}",
                "Substitution",
                mask,
                $"空缺处应该填 {expDiff} 还是 {typDiff}？",
                expDiff,
                typDiff,
                $"正确拼写是 {expDiff}：{left}·{expDiff}·{right}");
        }

        // 5. Missing or extra non-doubled letter (e.g. environment -> enviroment)
        {
            var anchorStart = Math.Max(0, prefix - (prefix > 0 ? 1 : 0));
            var anchorEnd = Math.Min(expected.Length, expected.Length - suffix + (suffix > 0 && anchorStart == prefix ? 1 : 0));
            var correctSpan = expected[anchorStart..anchorEnd];
            var typedEnd = Math.Clamp(typed.Length - (expected.Length - anchorEnd), anchorStart, typed.Length);
            var wrongSpan = typed[anchorStart..typedEnd];
            if (correctSpan.Length == 0 || wrongSpan.Length == 0 || correctSpan == wrongSpan)
                return null;
            var left = expected[..anchorStart];
            var right = expected[anchorEnd..];
            var mask = $"{left} ___ {right}".Trim();
            return new SpellingMistakeAnalysis(
                $"span:{anchorStart}:{correctSpan}:{wrongSpan}",
                "MissingOrExtra",
                mask,
                $"空缺处该写 {correctSpan} 还是 {wrongSpan}？",
                correctSpan,
                wrongSpan,
                $"正确拼写是 {correctSpan}：{left}·{correctSpan}·{right}");
        }
    }
}

public sealed class SpellingMistakeStore
{
    public const int MaxWords = 500;
    public const int MasteredCleanStreak = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, SpellingMistakeRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public SpellingMistakeStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoLex", "spelling-mistakes.json");
    }

    public int ActiveCount
    {
        get { lock (_gate) { EnsureLoaded(); return _records.Values.Count(r => r.CleanStreak < MasteredCleanStreak); } }
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
            var removed = _records.Count;
            _records.Clear();
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return removed;
        }
    }

    public SpellingMistakeRecord? RecordMistake(string word, string typed)
    {
        var analysis = SpellingMistakeAnalyzer.Analyze(word, typed);
        if (analysis == null) return null;
        var cleanWord = word.Trim();
        var cleanTyped = typed.Trim();
        lock (_gate)
        {
            EnsureLoaded();
            var count = 1;
            if (_records.TryGetValue(cleanWord, out var existing))
            {
                count = existing.PatternKey == analysis.PatternKey ? existing.MistakeCount + 1 : Math.Max(1, existing.MistakeCount);
            }
            var record = new SpellingMistakeRecord(
                cleanWord,
                analysis.PatternKey,
                analysis.Kind,
                cleanTyped,
                analysis.ContextMask,
                analysis.Question,
                analysis.CorrectOption,
                analysis.WrongOption,
                analysis.Explanation,
                count,
                0,
                DateTimeOffset.UtcNow);
            _records[cleanWord] = record;
            TrimLocked();
            PersistLocked();
            return record;
        }
    }

    public void RecordCleanSuccess(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        var cleanWord = word.Trim();
        lock (_gate)
        {
            EnsureLoaded();
            if (!_records.TryGetValue(cleanWord, out var existing)) return;
            var streak = existing.CleanStreak + 1;
            if (streak >= MasteredCleanStreak)
                _records.Remove(cleanWord);
            else
                _records[cleanWord] = existing with { CleanStreak = streak, UpdatedAt = DateTimeOffset.UtcNow };
            PersistLocked();
        }
    }

    public SpellingMicroDrill? GetActiveDrill(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        lock (_gate)
        {
            EnsureLoaded();
            if (!_records.TryGetValue(word.Trim(), out var record)) return null;
            if (record.CleanStreak >= MasteredCleanStreak) return null;
            return new SpellingMicroDrill(
                record.Word,
                record.ContextMask,
                record.Question,
                record.CorrectOption,
                record.WrongOption,
                record.Explanation,
                record.LastWrongInput,
                record.MistakeCount);
        }
    }

    public IReadOnlyList<SpellingMistakeRecord> ListActive()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _records.Values
                .Where(r => r.CleanStreak < MasteredCleanStreak)
                .OrderByDescending(r => r.MistakeCount)
                .ThenByDescending(r => r.UpdatedAt)
                .ToArray();
        }
    }

    private void TrimLocked()
    {
        if (_records.Count <= MaxWords) return;
        var keep = _records.Values
            .OrderByDescending(r => r.UpdatedAt)
            .Take(MaxWords)
            .ToDictionary(r => r.Word, StringComparer.OrdinalIgnoreCase);
        _records = keep;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_path)) return;
            var list = JsonSerializer.Deserialize<List<SpellingMistakeRecord>>(File.ReadAllText(_path), JsonOptions);
            if (list == null) return;
            _records = new Dictionary<string, SpellingMistakeRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.Word) && !string.IsNullOrWhiteSpace(item.CorrectOption))
                    _records[item.Word.Trim()] = item;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _records = new Dictionary<string, SpellingMistakeRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void PersistLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_records.Values.ToArray(), JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
