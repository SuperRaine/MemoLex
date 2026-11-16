using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemoLex.Core;
public sealed class Preferences
{
    public const int HistoryLimit = 500;
    public string? NotepadId { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool CloseToTray { get; set; }
    public bool RememberCloseAction { get; set; }
    public bool DoubleCopyEnabled { get; set; }
    public bool OnboardingCompleted {get;set;}
    public bool ShowExamCountdown {get;set;}
    public string ExamName {get;set;}="考试";
    public DateTime? ExamDate {get;set;}
    public string StubbornSort {get;set;}="review";
    public bool StubbornAlphabetDescending {get;set;}
    public bool HideStubbornMeanings {get;set;}
    public Dictionary<string,List<string>> ConfirmedLearnedWords {get;set;}=[];
    public bool HoverLookupEnabled {get;set;}
    public int HoverDelayMs {get;set;}=750;
    public int HoverModifiers {get;set;}
    public bool HoverNearest {get;set;}
    public bool HoverTranslucent {get;set;}
    public bool HoverAutoAdd {get;set;}
    public string HoverFontSize {get;set;}="standard";
    public bool AutomaticUpdates {get;set;}=true;
    public bool AutomaticUpdateDownloads {get;set;}
    public string UpdateFrequency {get;set;}="daily";
    public string UpdateSource {get;set;}="auto";
    public DateTimeOffset? LastUpdateCheck {get;set;}
    public string? LastPublicVersion {get;set;}
    public DateTimeOffset? LastSuccessfulUpdateCheck {get;set;}
    public string ExamplePaperType { get; set; } = "ALL";
    public Dictionary<string,string> Shortcuts {get;set;} = ShortcutDefinitions.All.ToDictionary(item=>item.Id,item=>item.Default);
    // Kept for migration and compatibility with the previous desktop version.
    public List<string> History { get; set; } = [];
    public List<SearchHistoryEntry> SearchHistory { get; set; } = [];
    private string _path = PathName;
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoLex", "preferences.json");
    public static Preferences Load(string? path = null)
    {
        path ??= PathName;
        Preferences result;
        try { result = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { result = new(); }
        result._path = path;
        result.NormalizeHistory();
        result.ExamName=string.IsNullOrWhiteSpace(result.ExamName)?"考试":result.ExamName.Trim();
        if(result.ExamName.Length>40)result.ExamName=result.ExamName[..40];
        result.ExamDate=result.ExamDate?.Date;
        if(result.StubbornSort is not ("review" or "alphabetical" or "random"))result.StubbornSort="review";
        result.ConfirmedLearnedWords=(result.ConfirmedLearnedWords??[])
            .Where(item=>!string.IsNullOrWhiteSpace(item.Key)&&item.Key.Length<=100&&item.Value!=null)
            .ToDictionary(item=>item.Key,item=>item.Value.Where(UsableWord).Select(word=>word.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),StringComparer.Ordinal);
        result.Shortcuts??=[];
        foreach(var item in ShortcutDefinitions.All)if(!result.Shortcuts.TryGetValue(item.Id,out var value)||value==null)result.Shortcuts[item.Id]=item.Default;
        return result;
    }
    private void NormalizeHistory()
    {
        SearchHistory ??= [];
        History ??= [];
        if (SearchHistory.Count == 0)
            SearchHistory = History.Where(UsableWord).Select(word => new SearchHistoryEntry { Word = word.Trim() }).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SearchHistory = SearchHistory.Where(item => item != null && UsableWord(item.Word))
            .Where(item => seen.Add(item.Word.Trim())).Take(HistoryLimit).ToList();
        foreach (var item in SearchHistory) { item.Word = item.Word.Trim(); item.LookupCount = Math.Max(1, item.LookupCount); }
        History = SearchHistory.Select(item => item.Word).ToList();
    }
    private static bool UsableWord(string? word) => !string.IsNullOrWhiteSpace(word) && word.Trim().Length <= 100 && !word.Any(char.IsControl);
    public bool IsConfirmedLearned(string? accountId,string word)=>accountId!=null&&ConfirmedLearnedWords.TryGetValue(accountId,out var words)&&words.Contains(word.Trim(),StringComparer.OrdinalIgnoreCase);
    public void ConfirmLearned(string accountId,string word)
    {
        if(string.IsNullOrWhiteSpace(accountId)||accountId.Length>100||!UsableWord(word))throw new ArgumentException("请选择有效账号和单词。");
        if(!ConfirmedLearnedWords.TryGetValue(accountId,out var words))ConfirmedLearnedWords[accountId]=words=[];
        if(!words.Contains(word.Trim(),StringComparer.OrdinalIgnoreCase))words.Add(word.Trim());
    }
    public void RecordLookup(string word, DateTimeOffset? when = null)
    {
        if (!UsableWord(word)) return;
        word = word.Trim();
        var previous = SearchHistory.FirstOrDefault(item => item.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
        SearchHistory.RemoveAll(item => item.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
        SearchHistory.Insert(0, new SearchHistoryEntry { Word = word, LastSearched = when ?? DateTimeOffset.Now,
            LookupCount = previous == null ? 1 : (int)Math.Min(int.MaxValue, (long)previous.LookupCount + 1) });
        if (SearchHistory.Count > HistoryLimit) SearchHistory.RemoveRange(HistoryLimit, SearchHistory.Count - HistoryLimit);
        History = SearchHistory.Select(item => item.Word).ToList();
    }
    public void RemoveLookup(string word)
    {
        SearchHistory.RemoveAll(item => item.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
        History = SearchHistory.Select(item => item.Word).ToList();
    }
    public void ClearHistory()
    {
        SearchHistory.Clear();
        History.Clear();
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, _path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed record ShortcutDefinition(string Id,string Label,string Default,bool Global=false);
public static class ShortcutDefinitions
{
    public static IReadOnlyList<ShortcutDefinition> All {get;} = [
        new("clipboard","查询剪贴板（全局）","Ctrl+Alt+D",true),
        new("screenshot","截图翻译（全局）","Ctrl+Alt+S",true),
        new("search","打开快速查词（全局）","Ctrl+L",true),new("history","查词历史","Ctrl+H"),
        new("toggleWord","加入或移出云词本","Ctrl+Enter"),new("back","后退","Alt+Left"),new("forward","前进","Alt+Right"),
        new("translation","打开段落翻译","Ctrl+T"),new("translate","开始段落翻译","Ctrl+Shift+Enter")];
}

public sealed class SearchHistoryEntry
{
    public string Word { get; set; } = "";
    public DateTimeOffset? LastSearched { get; set; }
    public int LookupCount { get; set; } = 1;
    [JsonIgnore] public string DisplayTime => LastSearched?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "较早的查询";
    public override string ToString() => Word;
}
