using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MemoLex.Core;

public sealed record ContextQuizWrongRecord(string ItemId, string ChosenLabel);

public sealed record ContextQuizProgressSnapshot(
    ContextQuizQuestion[] Questions,
    int CurrentIndex,
    int CorrectCount,
    int WrongCount,
    ContextQuizWrongRecord[] WrongRecords,
    Dictionary<string, int> MistakeKinds);

public static class ContextQuizProgressStore
{
    private const int MaxFileSize = 12 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static ContextQuizProgressSnapshot? Load(string profile, string? accountKey, IReadOnlySet<string>? validItemIds = null)
    {
        try
        {
            var path = PathFor(profile, accountKey);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            if (stream.Length is < 2 or > MaxFileSize) return null;
            var snapshot = JsonSerializer.Deserialize<ContextQuizProgressSnapshot>(stream, JsonOptions);
            return Normalize(snapshot, validItemIds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string profile, string? accountKey, ContextQuizProgressSnapshot snapshot)
    {
        var normalized = Normalize(snapshot, null);
        if (normalized == null)
        {
            Remove(profile, accountKey);
            return;
        }

        var path = PathFor(profile, accountKey);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(profile);
            using (var file = File.Create(staging))
                JsonSerializer.Serialize(file, normalized, JsonOptions);
            File.Move(staging, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { DeleteFile(staging); }
    }

    public static void Remove(string profile, string? accountKey) => DeleteFile(PathFor(profile, accountKey));

    public static (int Count, long Bytes) MeasureAll(string profile)
    {
        try
        {
            if (!Directory.Exists(profile)) return (0, 0);
            var files = Directory.GetFiles(profile, "context-quiz-*.json");
            long bytes = 0;
            foreach (var file in files)
            {
                try { bytes += new FileInfo(file).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return (files.Length, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (0, 0); }
    }

    public static int RemoveAll(string profile)
    {
        try
        {
            if (!Directory.Exists(profile)) return 0;
            var files = Directory.GetFiles(profile, "context-quiz-*.json");
            var removed = 0;
            foreach (var file in files)
            {
                try { File.Delete(file); removed++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return removed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static ContextQuizProgressSnapshot? Normalize(ContextQuizProgressSnapshot? snapshot, IReadOnlySet<string>? validItemIds)
    {
        if (snapshot is not { Questions: { Length: > 0 } rawQuestions }) return null;

        var filtered = new List<ContextQuizQuestion>(rawQuestions.Length);
        var removedBeforeCursor = 0;
        for (var i = 0; i < rawQuestions.Length; i++)
        {
            var q = rawQuestions[i];
            var valid = q != null
                && !string.IsNullOrWhiteSpace(q.ItemId)
                && q.Options is { Count: >= 2 }
                && (validItemIds == null || validItemIds.Contains(q.ItemId));
            if (valid)
                filtered.Add(q!);
            else if (i < snapshot.CurrentIndex)
                removedBeforeCursor++;
        }

        if (filtered.Count == 0) return null;
        var index = Math.Clamp(snapshot.CurrentIndex - removedBeforeCursor, 0, filtered.Count);
        if (index >= filtered.Count) return null;

        var keptIds = filtered.Select(q => q.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wrongRecords = (snapshot.WrongRecords ?? [])
            .Where(w => w != null && !string.IsNullOrWhiteSpace(w.ItemId) && !string.IsNullOrWhiteSpace(w.ChosenLabel) && keptIds.Contains(w.ItemId))
            .ToArray();
        var kinds = snapshot.MistakeKinds != null
            ? new Dictionary<string, int>(snapshot.MistakeKinds.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0), StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);

        return new ContextQuizProgressSnapshot(
            filtered.ToArray(),
            index,
            Math.Max(0, snapshot.CorrectCount),
            Math.Max(0, snapshot.WrongCount),
            wrongRecords,
            kinds);
    }

    private static string PathFor(string profile, string? accountKey)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(accountKey) ? "default" : accountKey)));
        return Path.Combine(profile, $"context-quiz-{key}.json");
    }

    private static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
