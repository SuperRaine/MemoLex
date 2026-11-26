using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoLex.Core;

namespace MemoLex.Services;

internal static class FlashcardProgressStore
{
    private const int MaxFileSize = 8 * 1024 * 1024;
    private const int MaxWords = 200_000;

    internal static FlashcardProgressSnapshot? Load(string profile, string accountId)
    {
        try
        {
            var path = PathFor(profile, accountId);
            if (!File.Exists(path)) return null;
            using var file = File.OpenRead(path);
            if (file.Length is < 1 or > MaxFileSize) return null;
            var snapshot = JsonSerializer.Deserialize<FlashcardProgressSnapshot>(file);
            return IsValid(snapshot) ? snapshot : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static void Save(string profile, string accountId, FlashcardProgressSnapshot snapshot)
    {
        if (!IsValid(snapshot)) return;
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

    private static bool IsValid(FlashcardProgressSnapshot? snapshot) =>
        snapshot is { Words: { Length: > 0 and <= MaxWords } words, Queue: { Length: <= MaxWords } queue }
        && snapshot.Cursor >= 0 && snapshot.Cursor <= words.Length
        && snapshot.GroupNumber >= 0 && snapshot.GroupWordCount >= 0
        && words.All(ValidWord) && queue.All(ValidWord);

    private static bool ValidWord(string? word) =>
        word is { Length: > 0 and <= 100 } && !word.Any(char.IsControl);

    private static string PathFor(string profile, string accountId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(accountId) ? "default" : accountId)));
        return Path.Combine(profile, $"stubborn-flashcards-{key}.json");
    }

    private static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
