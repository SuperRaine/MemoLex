using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MemoLex.Core;
public sealed class DictionaryEntry
{
    public string word { get; set; } = "";
    public string phonetic { get; set; } = "";
    public string definition { get; set; } = "";
    // Compact dictionaries retain only the traits used for Chinese search ranking.
    public byte? lexical_flags {get;set;}
    [JsonIgnore] internal byte LexicalFlags=>lexical_flags??(byte)((definition.Length>0?4:0)
        |(Regex.IsMatch(definition,@"(?:^|\n)[as](?:\.|\s)")?1:0)
        |(Regex.IsMatch(definition,@"\b(?:archaic|obsolete|dialectal)\b",RegexOptions.IgnoreCase)?2:0));
    [JsonIgnore] internal bool HasEnglishDefinition=>(LexicalFlags&4)!=0;
    [JsonIgnore] internal bool HasAdjectiveDefinition=>(LexicalFlags&1)!=0;
    [JsonIgnore] internal bool HasHistoricalDefinition=>(LexicalFlags&2)!=0;
    public string translation { get; set; } = "";
    public string pos { get; set; } = "";
    public string collins { get; set; } = "";
    public string oxford { get; set; } = "";
    public string tag { get; set; } = "";
    public string bnc { get; set; } = "";
    public string frq { get; set; } = "";
    public string exchange { get; set; } = "";
    public string source {get;set;}="";
}

public sealed class LocalDictionary : IDisposable
{
    private readonly Dictionary<string, (long Offset, int Length)> _entries = new(StringComparer.OrdinalIgnoreCase);
    private string[] _keys = [];
    private readonly List<string> _sourceWords=[];
    private FileStream? _data;
    private readonly object _gate = new();
    private readonly string _directory;
    internal static string? ReadConfiguredBriefPath(string? settingsPath=null)
    {
        if(settingsPath==null)
        {
            var environmentPath=Environment.GetEnvironmentVariable("MEMOLEX_LOCAL_DICTIONARY");
            if(!string.IsNullOrWhiteSpace(environmentPath))return environmentPath.Trim();
            settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoLex","dictionary.path");
        }
        try
        {
            var path=File.Exists(settingsPath)?File.ReadAllText(settingsPath).Trim():null;
            return string.IsNullOrWhiteSpace(path)?null:path;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return null;}
    }
    private readonly LocalBriefDictionary? _preferred;
    public LocalDictionary(string? directory = null,string? preferredPath=null)
    {
        _directory = directory ?? Path.Combine(ApplicationPaths.RuntimeDirectory, "data");
        preferredPath??=directory==null?ReadConfiguredBriefPath():null;
        if(!string.IsNullOrWhiteSpace(preferredPath))_preferred=new(preferredPath);
    }
    internal LocalDictionary(string directory,LocalBriefDictionary preferred) : this(directory)
    {
        _preferred=preferred;
    }
    public int Count => _entries.Count;
    public int PreferredCount=>_preferred?.Count??0;
    public bool PreferredAvailable=>_preferred?.Available==true;
    public string? PreferredWarning=>_preferred?.LoadWarning;
    public Task LoadAsync()=>Task.WhenAll(LoadBaseAsync(),_preferred?.LoadAsync()??Task.CompletedTask);
    private Task LoadBaseAsync() => Task.Run(() =>
    {
        if(_data!=null)return;
        var directory = _directory;
        foreach (var line in File.ReadLines(Path.Combine(directory, "dictionary.index.tsv")))
        {
            var parts = line.Split('\t');
            if (parts.Length == 3 && long.TryParse(parts[1], CultureInfo.InvariantCulture, out var offset)
                && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var length))
            {
                _entries.TryAdd(parts[0], (offset, length));
                _sourceWords.Add(parts[0]);
            }
        }
        _keys = _entries.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _data = new FileStream(Path.Combine(directory, "dictionary.data"), FileMode.Open, FileAccess.Read, FileShare.Read);
    });

    public DictionaryEntry? Lookup(string word)
    {
        var entry=LookupBase(word);var local=_preferred?.Lookup(word);
        if(!string.IsNullOrWhiteSpace(local?.translation))
        {
            entry??=local;entry.translation=local.translation;entry.source="local-brief";
        }
        return entry;
    }
    private DictionaryEntry? LookupBase(string word)
    {
        if (_data == null || !_entries.TryGetValue(word.Trim(), out var address)) return null;
        lock (_gate)
        {
            var bytes = new byte[address.Length];
            _data.Position = address.Offset;
            _data.ReadExactly(bytes);
            return JsonSerializer.Deserialize<DictionaryEntry>(bytes);
        }
    }
    internal int SourceRowCount=>_sourceWords.Count;
    internal string SourceWord(int id)=>_sourceWords[id];
    internal string IndexPath=>Path.Combine(_directory,"dictionary.index.tsv");
    internal IEnumerable<DictionaryEntry> EnumeratePreferredEntries()=>_preferred?.EnumerateEntries()??[];
    // Legacy postings and WordNet IDs identify ECDICT rows. Resolve their words
    // through Lookup when showing preferred definitions; do not reinterpret IDs.
    internal DictionaryEntry? LookupBySourceId(int id)=>id>=0&&id<_sourceWords.Count?LookupBase(_sourceWords[id]):null;
    public IReadOnlyList<string> Suggest(string prefix, int count = 20)
    {
        prefix = prefix.Trim();
        if (prefix.Length == 0 || _keys.Length == 0) return Array.Empty<string>();
        var index = Array.BinarySearch(_keys, prefix, StringComparer.OrdinalIgnoreCase);
        if (index < 0) index = ~index;
        var result = new List<string>();
        for (; index < _keys.Length && result.Count < count; index++)
        {
            if (!_keys[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) break;
            result.Add(_keys[index]);
        }
        return result;
    }
    public void Dispose() => _data?.Dispose();
}
