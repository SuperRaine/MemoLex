using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public readonly record struct RelatedSense(string PartOfSpeech, string Meaning);
public sealed record RelatedWord(string Word, string Meaning)
{
    public IEnumerable<RelatedSense> Senses => Meaning
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line =>
        {
            var (label, text) = DictionaryEntry.SplitDisplaySense(line);
            return new RelatedSense(label, text);
        });
}
public sealed record WordRelations(IReadOnlyList<RelatedWord> Synonyms, IReadOnlyList<RelatedWord> Similar, IReadOnlyList<RelatedWord> Derivatives,IReadOnlyList<RelatedWord> Phrases);
public sealed class RelatedWords(LocalDictionary dictionary, string? directory = null) : IDisposable
{
    private readonly string _directory = directory ?? Path.Combine(ApplicationPaths.RuntimeDirectory,"data");
    private readonly RelatedPhrases _phrases=new(dictionary);
    private ChineseReverseSearch? _chinese;
    private readonly Dictionary<string,(long Offset,int Length)> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Word,int Rank)> _spellings = [];
    private readonly object _gate=new();
    private FileStream? _data;
    private static readonly Regex NonstandardSpelling = new(
        @"^\s*(?:abbr|pref|suf|comb|contr)\.|^(?:\[[^\]]*\]\s*)?(?:[a-z]+\.\s*)?(?:\[\s*)?(?:古英语|古义|古语|古用法|方言|古(?:\s|\]))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnnotationPattern = new(@"\[[^\]]*\]|\([^)]*\)|（[^）]*）", RegexOptions.Compiled);
    private static readonly Regex HanTokenPattern = new(@"[\u4E00-\u9FFF]{2,4}", RegexOptions.Compiled);
    private static readonly Lazy<Dictionary<string, string>> DerivativeParts = new(LoadDerivativeParts);
    public bool Available => _data!=null;
    public void AttachChineseSearch(ChineseReverseSearch chinese) => _chinese = chinese;
    public IReadOnlyList<RelatedWord> LookupPhrases(string word, CancellationToken cancellationToken = default) => _phrases.Lookup(word, cancellationToken);
    private sealed class Record
    {
        public string[] synonyms {get;set;}=[];
        public string[] derivatives {get;set;}=[];
        public string gloss {get;set;}="";
    }
    public Task LoadAsync() => Task.Run(() =>
    {
        var index=Path.Combine(_directory,"related.index.tsv"); var data=Path.Combine(_directory,"related.data");
        if(!File.Exists(index)||!File.Exists(data)) return;
        foreach(var line in File.ReadLines(index))
        {
            var fields=line.Split('\t');
            if(fields.Length==3 && long.TryParse(fields[1],out var offset) && int.TryParse(fields[2],out var length)) _index.TryAdd(fields[0],(offset,length));
        }
        _data=File.Open(data,FileMode.Open,FileAccess.Read,FileShare.Read);
        var spellings=Path.Combine(_directory,"similar-words.tsv");
        if(File.Exists(spellings)) foreach(var line in File.ReadLines(spellings))
        {
            var fields=line.Split('\t'); if(fields.Length==2 && int.TryParse(fields[1],out var rank)) _spellings.Add((fields[0],rank));
        }
    });
    private Record? Read(string word)
    {
        if(_data==null||!_index.TryGetValue(word,out var item))return null;
        lock(_gate) { var bytes=new byte[item.Length];_data.Position=item.Offset;_data.ReadExactly(bytes);return JsonSerializer.Deserialize<Record>(bytes); }
    }
    private static Dictionary<string, string> LoadDerivativeParts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(RelatedWords).Assembly.GetManifestResourceStream("MemoLex.DerivativeParts.tsv");
        if (stream == null) return result;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t');
            if (tab > 0 && tab < line.Length - 1) result.TryAdd(line[..tab], line[(tab + 1)..]);
        }
        return result;
    }
    internal IReadOnlyList<string> SynonymSpellings(string word)
    {
        try{return Read(word)?.synonyms??[];}
        catch(Exception error) when(error is IOException or JsonException or ObjectDisposedException){return [];}
    }
    public async Task<WordRelations> LookupAsync(string word,CancellationToken cancellationToken=default)
    {
        var (record, similar, derivatives, phrases) = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rec = Read(word);
            var nearby = new List<(string Word, int Distance, int Rank)>();
            var normalized = word.ToLowerInvariant();
            if (word.Length is >= 3 and <= 24 && word.All(char.IsAsciiLetter))
                foreach (var item in _spellings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Word.Equals(word, StringComparison.OrdinalIgnoreCase) || Math.Abs(item.Word.Length - word.Length) > 2) continue;
                    var distance = SpellingDistance(normalized, item.Word, 2);
                    if (distance == 2 && word.Length <= 5 && item.Rank > 15000) continue;
                    if (distance <= 2) nearby.Add((item.Word, distance, item.Rank));
                }
            var simList = new List<RelatedWord>(8);
            foreach (var candidate in nearby.OrderBy(value => value.Distance).ThenBy(value => value.Rank).ThenBy(value => value.Word, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = dictionary.Lookup(candidate.Word);
                if (entry == null || entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(entry.translation) ||
                    !entry.translation.Any(c => c is >= '\u3400' and <= '\u9fff')) continue;

                var meaning = entry.DisplayMeaning;
                if (meaning.Length == 0) continue;
                simList.Add(new RelatedWord(entry.word, meaning));
                if (simList.Count == 8) break;
            }
            return (rec, (IReadOnlyList<RelatedWord>)simList, DescribeWords(word, rec?.derivatives ?? [], supplementPartOfSpeech: true, preferFrequency: true), _phrases.Lookup(word, cancellationToken));
        }, cancellationToken);

        var synonyms = await BuildSynonymsAsync(word, record?.synonyms ?? [], cancellationToken);
        return new WordRelations(synonyms, similar, derivatives, phrases);
    }

    private IReadOnlyList<RelatedWord> DescribeWords(string word, IEnumerable<string> words, bool supplementPartOfSpeech = false, bool preferFrequency = false)
    {
        var items = words.Where(value => value.Length is > 0 and <= 100 && char.IsAsciiLetter(value[0])
            && value.All(character => char.IsAsciiLetter(character) || character is ' ' or '.' or '’' or '\'' or '-')
            && !value.Equals(word, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => (Word: value, Entry: dictionary.Lookup(value)));

        var ordered = preferFrequency
            ? items
                .OrderBy(v => string.IsNullOrWhiteSpace(v.Entry?.translation) ? 1 : 0)
                .ThenBy(v => v.Entry != null && (v.Entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(v.Entry.translation)) ? 1 : 0)
                .ThenBy(v => v.Word.Contains(' ') ? 1 : 0)
                .ThenBy(v => dictionary.FrequencyRank(v.Word))
            : items.OrderBy(v => string.IsNullOrWhiteSpace(v.Entry?.translation) ? 1 : 0);

        return ordered.Take(12).Select(value =>
        {
            var meaning = value.Entry?.DisplayMeaning ?? "";
            if (meaning.Length == 0) meaning = DictionaryEntry.FormatDisplayMeaning(Read(value.Word)?.gloss ?? "暂无释义");
            if (supplementPartOfSpeech && meaning.Length > 0 && DerivativeParts.Value.TryGetValue(value.Word, out var partOfSpeech)
                && !meaning.Split('\n').Any(line => DictionaryEntry.SplitDisplaySense(line).PartOfSpeech.Length > 0))
                meaning = partOfSpeech + " " + meaning;
            return new RelatedWord(value.Word, meaning);
        }).ToArray();
    }

    private async Task<IReadOnlyList<RelatedWord>> BuildSynonymsAsync(string word, IReadOnlyList<string> rawWordNet, CancellationToken cancellationToken)
    {
        var baseEntry = dictionary.Lookup(word);
        var baseDisplay = baseEntry?.DisplayMeaning ?? "";
        var firstPos = baseDisplay.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => DictionaryEntry.SplitDisplaySense(line).PartOfSpeech.Trim().ToLowerInvariant())
            .FirstOrDefault() ?? "";
        var isFunctionWord = firstPos is "prep." or "conj." or "pron." or "det." or "art." or "aux.";

        var wordNetList = DescribeWords(word, rawWordNet, supplementPartOfSpeech: false, preferFrequency: true);

        if (_chinese == null || !_chinese.Available || word.Contains(' ') || string.IsNullOrWhiteSpace(baseDisplay))
            return wordNetList;

        // Supplement via Chinese semantic overlap when WordNet has few synonyms or for function words (prepositions/conjunctions)
        // where WordNet often lacks entries (e.g. "against" -> "oppose", "despite" -> "although").
        if (!isFunctionWord && wordNetList.Count >= 8)
            return wordNetList;

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { word };
        if (!string.IsNullOrWhiteSpace(baseEntry?.exchange))
        {
            foreach (var part in baseEntry.exchange.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = part.IndexOf(':');
                if (colon > 0 && colon < part.Length - 1) excluded.Add(part[(colon + 1)..].Trim());
            }
        }

        var coreGlosses = ExtractCoreChineseGlosses(baseDisplay);
        if (coreGlosses.Count == 0) return wordNetList;

        var stem = word.ToLowerInvariant();
        if (stem.Length > 4 && stem.EndsWith('e')) stem = stem[..^1];
        var supplemental = new List<(string Word, string Meaning, int GlossIndex, double Score, int Rank)>();
        var seenSup = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);

        for (var gi = 0; gi < Math.Min(3, coreGlosses.Count); gi++)
        {
            var gloss = coreGlosses[gi];
            cancellationToken.ThrowIfCancellationRequested();
            ChineseSearchPage page;
            try { page = await _chinese.SearchAsync(gloss, 18, cancellationToken); }
            catch (Exception ex) when (ex is ArgumentException or IOException) { continue; }

            foreach (var hit in page.Items)
            {
                var w = hit.Word.Trim();
                if (w.Length is < 2 or > 28 || w.Contains(' ') || w.Contains('-') || !w.All(char.IsAsciiLetter)) continue;
                var lowW = w.ToLowerInvariant();
                if (stem.Length >= 5 && lowW.StartsWith(stem, StringComparison.Ordinal)) continue;
                if (!seenSup.Add(w)) continue;
                var entry = dictionary.Lookup(w);
                if (entry == null || entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(entry.translation)) continue;
                var rank = dictionary.FrequencyRank(w);
                if (rank > 45000 && !hit.IsExactMatch) continue;
                var display = entry.DisplayMeaning;
                if (string.IsNullOrWhiteSpace(display)) continue;
                supplemental.Add((entry.word, display, gi, hit.Score, rank));
            }
        }

        if (supplemental.Count == 0) return wordNetList;

        var rankedSup = supplemental
            .OrderBy(x => x.GlossIndex)
            .ThenByDescending(x => x.Score >= 1000 ? 1 : 0)
            .ThenBy(x => x.Rank)
            .ThenByDescending(x => x.Score)
            .Select(x => new RelatedWord(x.Word, x.Meaning))
            .ToList();

        var merged = new List<RelatedWord>(12);
        var added = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);

        // For function words (e.g. "against", "despite"), Chinese core gloss synonyms reflect the real primary sense better than archaic WordNet noun senses.
        var primary = isFunctionWord ? (IEnumerable<RelatedWord>)rankedSup : wordNetList;
        var secondary = isFunctionWord ? (IEnumerable<RelatedWord>)wordNetList : rankedSup;

        foreach (var item in primary.Concat(secondary))
        {
            if (added.Add(item.Word))
            {
                merged.Add(item);
                if (merged.Count >= 12) break;
            }
        }
        return merged;
    }

    private static List<string> ExtractCoreChineseGlosses(string displayMeaning)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines.Take(2))
        {
            var (_, rest) = DictionaryEntry.SplitDisplaySense(line);
            var cleaned = AnnotationPattern.Replace(rest.Length > 0 ? rest : line, "");
            foreach (var seg in cleaned.Split(['，', ',', '；', ';', '、', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var token = seg.Trim();
                if (token.Length > 2 && (token.EndsWith('的') || token.EndsWith('地'))) token = token[..^1];
                if (token.Length is >= 2 and <= 4 && token.All(c => c is >= '\u4E00' and <= '\u9FFF'))
                {
                    if (seen.Add(token)) results.Add(token);
                }
                else
                {
                    var m = HanTokenPattern.Match(token);
                    if (m.Success && seen.Add(m.Value)) results.Add(m.Value);
                }
                if (results.Count >= 4) return results;
            }
        }
        return results;
    }
    // Optimal string alignment distance treats an adjacent transposition as one edit.
    internal static int SpellingDistance(string a,string b,int maximum)
    {
        if(Math.Abs(a.Length-b.Length)>maximum)return maximum+1;
        var previous=Enumerable.Range(0,b.Length+1).ToArray();var older=(int[])previous.Clone();var current=new int[b.Length+1];
        for(var i=1;i<=a.Length;i++)
        {
            Array.Fill(current,maximum+1);current[0]=i;var best=maximum+1;
            for(var j=Math.Max(1,i-maximum);j<=Math.Min(b.Length,i+maximum);j++)
            {
                current[j]=Math.Min(previous[j]+1,Math.Min(current[j-1]+1,previous[j-1]+(a[i-1]==b[j-1]?0:1)));
                if(i>1&&j>1&&a[i-1]==b[j-2]&&a[i-2]==b[j-1])current[j]=Math.Min(current[j],older[j-2]+1);
                best=Math.Min(best,current[j]);
            }
            if(best>maximum)return maximum+1;
            (older,previous,current)=(previous,current,older);
        }
        return previous[b.Length];
    }
    public void Dispose()=>_data?.Dispose();
}
