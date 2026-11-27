using System.IO;

namespace MemoLex.Desktop;

internal sealed record CacheCategoryItem(
    string Id,
    string Title,
    string Description,
    string Summary,
    bool HasData,
    string ConfirmMessage);

internal sealed class CacheMaintenanceHost(
    Func<IReadOnlyList<CacheCategoryItem>> loadCategories,
    Func<string, string> clearCategory,
    Func<string> clearAll)
{
    public IReadOnlyList<CacheCategoryItem> LoadCategories() => loadCategories();
    public string ClearCategory(string id) => clearCategory(id);
    public string ClearAll() => clearAll();

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }

    public static long MeasureDirectoryBytes(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    public static int ClearDirectoryBestEffort(string path)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(path)) return 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); removed++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(dir, recursive: false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }
}
