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
    private readonly Dictionary<string, int> _frequencyRanks = new(StringComparer.OrdinalIgnoreCase);
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
        var dataPath = Path.Combine(directory, "dictionary.data");
        using var frequencyData = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, FileOptions.SequentialScan);
        var dataLength = frequencyData.Length;
        var record = new byte[1024];
        foreach (var line in File.ReadLines(Path.Combine(directory, "dictionary.index.tsv")))
        {
            var parts = line.Split('\t');
            if (parts.Length == 3 && long.TryParse(parts[1], CultureInfo.InvariantCulture, out var offset)
                && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var length))
            {
                var word = parts[0];
                _sourceWords.Add(word);
                if (!_entries.TryAdd(word, (offset, length)) || offset < 0 || length <= 0 || offset > dataLength - length)
                    continue;
                if (record.Length < length) Array.Resize(ref record, length);
                if (frequencyData.Position != offset) frequencyData.Position = offset;
                var entry = record.AsSpan(0, length);
                frequencyData.ReadExactly(entry);
                var rank = ReadFrequencyRank(entry);
                if (rank != int.MaxValue) _frequencyRanks.Add(word, rank);
            }
        }
        _keys = _entries.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _data = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
    public int FrequencyRank(string word)=>_frequencyRanks.GetValueOrDefault(word.Trim(),int.MaxValue);
    public IReadOnlyList<string> SuggestSpellingCorrections(string query, int limit = 5)
    {
        query = query.Trim().ToLowerInvariant();
        if (limit <= 0 || query.Length is < 3 or > 30 || _keys.Length == 0) return Array.Empty<string>();
        foreach (var ch in query)
            if (ch is not ((>= 'a' and <= 'z') or '-' or '\'')) return Array.Empty<string>();

        var maxDist = query.Length <= 4 ? 1 : 2;
        var q0 = query[0];
        var q1 = query[1];
        var scored = new List<(string Word, int Distance, int Rank)>(64);

        foreach (var candidate in _keys)
        {
            var lenDiff = candidate.Length - query.Length;
            if (lenDiff < -maxDist || lenDiff > maxDist || candidate.Length < 2) continue;
            var c0 = char.ToLowerInvariant(candidate[0]);
            var c1 = char.ToLowerInvariant(candidate[1]);
            if (c0 != q0 && c0 != q1 && c1 != q0 && c1 != q1) continue;

            var validChars = true;
            for (var i = 0; i < candidate.Length; i++)
            {
                var ch = candidate[i];
                if (!((ch is >= 'a' and <= 'z') || (ch is >= 'A' and <= 'Z') || ch is '-' or '\''))
                {
                    validChars = false;
                    break;
                }
            }
            if (!validChars) continue;

            var distance = BoundedDamerauLevenshtein(query, candidate, maxDist);
            if (distance > 0 && distance <= maxDist)
            {
                scored.Add((candidate, distance, _frequencyRanks.GetValueOrDefault(candidate, int.MaxValue)));
            }
        }

        if (scored.Count == 0) return Array.Empty<string>();
        return scored
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Rank == int.MaxValue ? 1 : 0)
            .ThenBy(item => item.Rank)
            .ThenBy(item => Math.Abs(item.Word.Length - query.Length))
            .ThenBy(item => item.Word, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Word)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    private static int BoundedDamerauLevenshtein(string source, string target, int maxDistance)
    {
        var n = source.Length;
        var m = target.Length;
        if (Math.Abs(n - m) > maxDistance) return maxDistance + 1;
        var width = m + 1;
        Span<int> prevPrev = stackalloc int[width];
        Span<int> prev = stackalloc int[width];
        Span<int> curr = stackalloc int[width];

        for (var j = 0; j <= m; j++) prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            var si = source[i - 1];
            var siPrev = i > 1 ? source[i - 2] : '\0';

            for (var j = 1; j <= m; j++)
            {
                var tj = char.ToLowerInvariant(target[j - 1]);
                var cost = si == tj ? 0 : 1;
                var value = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);

                if (i > 1 && j > 1 && si == char.ToLowerInvariant(target[j - 2]) && siPrev == tj)
                    value = Math.Min(value, prevPrev[j - 2] + 1);

                curr[j] = value;
                if (value < rowMin) rowMin = value;
            }
            if (rowMin > maxDistance) return maxDistance + 1;

            prev.CopyTo(prevPrev);
            curr.CopyTo(prev);
        }
        return prev[m];
    }
    public IReadOnlyList<string> Suggest(string prefix, int count = 20)
    {
        prefix = prefix.Trim();
        if (prefix.Length == 0 || count <= 0 || _keys.Length == 0) return Array.Empty<string>();
        var index = Array.BinarySearch(_keys, prefix, StringComparer.OrdinalIgnoreCase);
        if (index < 0) index = ~index;
        var end = index;
        while (end < _keys.Length && _keys[end].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) end++;

        // The keys already supply the alphabetical tie-break for equal or missing ranks.
        return _keys.Skip(index).Take(end - index)
            .OrderBy(word => word.Length != prefix.Length)
            .ThenBy(word => word.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '\'' or '\u2019')))
            .ThenBy(word => _frequencyRanks.GetValueOrDefault(word, int.MaxValue))
            .Take(count).ToArray();
    }

    private static int ReadFrequencyRank(ReadOnlySpan<byte> entry)
    {
        var rank = int.MaxValue;
        try
        {
            var reader = new Utf8JsonReader(entry);
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
                var isFrequency = reader.ValueTextEquals("bnc"u8) || reader.ValueTextEquals("frq"u8);
                if (!reader.Read()) break;
                if (isFrequency)
                {
                    var value = 0;
                    var valid = reader.TokenType == JsonTokenType.String
                        ? int.TryParse(reader.ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                        : reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out value);
                    if (valid && value > 0) rank = Math.Min(rank, value);
                }
                reader.Skip();
            }
        }
        catch (JsonException) { }
        return rank;
    }
    public void Dispose() => _data?.Dispose();
}
