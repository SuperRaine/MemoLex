using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MemoLex.Services;

internal static class StubbornWordCache
{
    internal static StubbornWordList? Load(string profile, string accountId)
    {
        try
        {
            using var file = File.OpenRead(PathFor(profile, accountId));
            if (file.Length is < 1 or > 32 * 1024 * 1024) return null;
            var snapshot = JsonSerializer.Deserialize<StubbornWordList>(file);
            return snapshot?.Words is { } words && snapshot.Total >= words.Count
                && words.All(word => word is { Spelling.Length: > 0 and <= 100 } && !word.Spelling.Any(char.IsControl))
                ? snapshot : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static void Save(string profile, string accountId, StubbornWordList snapshot)
    {
        var path = PathFor(profile, accountId);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(profile);
            using (var file = File.Create(staging)) JsonSerializer.Serialize(file, snapshot);
            File.Move(staging, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { DeleteFile(staging); }
    }

    internal static void Remove(string profile, string accountId) => DeleteFile(PathFor(profile, accountId));

    internal static (int Count, long Bytes) MeasureAll(string profile)
    {
        try
        {
            if (!Directory.Exists(profile)) return (0, 0);
            var files = Directory.GetFiles(profile, "stubborn-*.json")
                .Where(f => !Path.GetFileName(f).StartsWith("stubborn-flashcards-", StringComparison.OrdinalIgnoreCase))
                .ToArray();
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

    internal static int RemoveAll(string profile)
    {
        try
        {
            if (!Directory.Exists(profile)) return 0;
            var files = Directory.GetFiles(profile, "stubborn-*.json")
                .Where(f => !Path.GetFileName(f).StartsWith("stubborn-flashcards-", StringComparison.OrdinalIgnoreCase))
                .ToArray();
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

    private static string PathFor(string profile, string accountId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)));
        return Path.Combine(profile, $"stubborn-{key}.json");
    }

    private static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
