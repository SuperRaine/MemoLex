using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MemoLex.Core;
public sealed class DictionaryEntry
{
    private const string PartOfSpeech = @"(?:adj|adv|prep|pron|conj|interj|int|n|v|vt|vi|aux|art|det|num|part|ptcp|contr|a|ad|s|abbr|pref|suf|comb|vbl|phr|un|na|pl|pp)\.";
    private static readonly Regex ListPartOfSpeech = new(
        $@"^{PartOfSpeech}(?:(?:\s*[/&,]\s*|\s+){PartOfSpeech})*(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MeaningWhitespace = new(@"\s+");
    // Preserve lexical periods, decimal points and omission marks while removing full stops from the gloss.
    private static readonly Regex GlossPeriods = new(
        @"(?<ellipsis>\.{2,}|．{2,})|(?<keep>(?<=[a-z])\.(?=[a-z])|(?<=\d)[.．](?=\d)|(?<=[a-z]\.[a-z])\.|\b(?:etc|vs|cf|sb|sth|no|mr|mrs|ms|dr|sr|jr)\.(?![a-z]))|[.．。]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DuplicateColons = new(@"([：:])(?:\s*[：:])+", RegexOptions.CultureInvariant);
    private static readonly Regex ChineseGlossPunctuation = new(@"[,:!?()\[\]{}/<>""]|(?<![a-z0-9])'|'(?![a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChinesePunctuationSpacing = new(@"\s*([，：；！？、])\s*", RegexOptions.CultureInvariant);
    private static readonly char[] StraySeparators = [' ', '\t', '：', ':', '，', ',', '、'];

    private string _translation = "";
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
    public string translation
    {
        get => _translation;
        set => _translation = NormalizeMeaning(value);
    }
    internal static string NormalizeMeaning(string? value) => value?.Replace(';', '；') ?? "";

    internal string DisplayMeaning => FormatDisplayMeaning(word.Contains(' ') ? RelatedPhrases.CorrectMeaning(word, translation) : translation);

    internal static bool IsPartOfSpeechLabel(string word) => ListPartOfSpeech.IsMatch(word + ".");

    internal static (string PartOfSpeech, string Meaning) SplitDisplaySense(string line)
    {
        var label = ListPartOfSpeech.Match(line);
        return (label.Value, line[label.Length..].TrimStart());
    }

    internal static string FormatDisplayMeaning(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var senses = value.Replace(@"\r\n", "\n").Replace(@"\r", "\n").Replace(@"\n", "\n")
            .Split(['\r', '\n', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new StringBuilder();
        string? partOfSpeech = null;
        string? displayedPartOfSpeech = null;
        foreach (var sense in senses)
        {
            var label = ListPartOfSpeech.Match(sense);
            if (label.Success) partOfSpeech = MeaningWhitespace.Replace(label.Value, " ");
            var gloss = sense[label.Length..];
            var text = GlossPeriods.Replace(gloss, match =>
            {
                if (match.Groups["keep"].Success) return match.Value;
                if (match.Groups["ellipsis"].Success) return "…";
                var before = match.Index - 1;
                var after = match.Index + match.Length;
                return before >= 0 && after < gloss.Length &&
                    char.IsLetterOrDigit(gloss[before]) && char.IsLetterOrDigit(gloss[after]) ? " " : "";
            });
            var meaning = DuplicateColons.Replace(MeaningWhitespace.Replace(text, " "), "$1").Trim(StraySeparators);
            if (meaning.Length == 0) continue;
            if (ChineseReverseSearch.ContainsChinese(meaning))
            {
                meaning = ChineseGlossPunctuation.Replace(meaning, match => ((char)(match.Value[0] + 0xfee0)).ToString());
                meaning = ChinesePunctuationSpacing.Replace(meaning, "$1");
            }

            if (!string.Equals(partOfSpeech, displayedPartOfSpeech, StringComparison.OrdinalIgnoreCase))
            {
                if (result.Length > 0) result.Append('\n');
                result.Append(partOfSpeech).Append(' ');
                displayedPartOfSpeech = partOfSpeech;
            }
            else if (result.Length > 0) result.Append('；');
            result.Append(meaning);
        }
        return result.ToString();
    }

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
    public LocalDictionary(string? directory = null)
    {
        _directory = directory ?? Path.Combine(ApplicationPaths.RuntimeDirectory, "data");
    }
    public int Count => _entries.Count;
    public Task LoadAsync() => Task.Run(() =>
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
    internal DictionaryEntry? LookupBySourceId(int id)=>id>=0&&id<_sourceWords.Count?Lookup(_sourceWords[id]):null;
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
