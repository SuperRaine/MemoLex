using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record RelatedWord(string Word, string Meaning);
public sealed record WordRelations(IReadOnlyList<RelatedWord> Synonyms, IReadOnlyList<RelatedWord> Similar, IReadOnlyList<RelatedWord> Derivatives,IReadOnlyList<RelatedWord> Phrases);
public sealed class RelatedWords(LocalDictionary dictionary, string? directory = null) : IDisposable
{
    private readonly string _directory = directory ?? Path.Combine(ApplicationPaths.RuntimeDirectory,"data");
    private readonly RelatedPhrases _phrases=new(dictionary);
    private readonly Dictionary<string,(long Offset,int Length)> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Word,int Rank)> _spellings = [];
    private readonly object _gate=new();
    private FileStream? _data;
    private static readonly Regex NonstandardSpelling = new(
        @"^\s*(?:abbr|pref|suf|comb|contr)\.|^(?:\[[^\]]*\]\s*)?(?:[a-z]+\.\s*)?(?:\[\s*)?(?:古英语|古义|古语|古用法|方言|古(?:\s|\]))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public bool Available => _data!=null;
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
    internal IReadOnlyList<string> SynonymSpellings(string word)
    {
        try{return Read(word)?.synonyms??[];}
        catch(Exception error) when(error is IOException or JsonException or ObjectDisposedException){return [];}
    }
    public Task<WordRelations> LookupAsync(string word,CancellationToken cancellationToken=default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record=Read(word);
        IReadOnlyList<RelatedWord> Describe(IEnumerable<string> words) => words.Where(value=>value.Length is >0 and <=100 && char.IsAsciiLetter(value[0])
            && value.All(character=>char.IsAsciiLetter(character)||character is ' ' or '.' or '’' or '\'' or '-')&&!value.Equals(word,StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(value=>(Word:value,Entry:dictionary.Lookup(value)))
            .OrderBy(value=>string.IsNullOrWhiteSpace(value.Entry?.translation)?1:0).Take(12)
            .Select(value =>
            {
                var meaning = value.Entry?.DisplayMeaning ?? "";
                if (meaning.Length == 0) meaning = DictionaryEntry.FormatDisplayMeaning(Read(value.Word)?.gloss ?? "暂无释义");
                return new RelatedWord(value.Word, meaning);
            }).ToArray();
        var nearby=new List<(string Word,int Distance,int Rank)>();var normalized=word.ToLowerInvariant();
        if(word.Length is >=3 and <=24 && word.All(char.IsAsciiLetter))
            foreach(var item in _spellings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(item.Word.Equals(word,StringComparison.OrdinalIgnoreCase)||Math.Abs(item.Word.Length-word.Length)>2)continue;
                var distance=SpellingDistance(normalized,item.Word,2);
                if (distance == 2 && word.Length <= 5 && item.Rank > 15000) continue;
                if(distance<=2)nearby.Add((item.Word,distance,item.Rank));
            }
        var similar = new List<RelatedWord>(8);
        foreach (var candidate in nearby.OrderBy(value => value.Distance).ThenBy(value => value.Rank).ThenBy(value => value.Word, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = dictionary.Lookup(candidate.Word);
            if (entry == null || entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(entry.translation) ||
                !entry.translation.Any(c => c is >= '\u3400' and <= '\u9fff')) continue;

            var meaning = entry.DisplayMeaning;
            if (meaning.Length == 0) continue;
            similar.Add(new RelatedWord(entry.word, meaning));
            if (similar.Count == 8) break;
        }
        return new WordRelations(Describe(record?.synonyms??[]),similar,Describe(record?.derivatives??[]),_phrases.Lookup(word,cancellationToken));
    },cancellationToken);
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
