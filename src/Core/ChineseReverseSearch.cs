using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record ChineseSearchResult(string Word,string Meaning,string MatchedMeaning,string Kind,double Score)
{
    public bool IsExactMatch {get;init;}
    public string Source {get;init;}="ecdict";
    public bool IsPreferredSource=>Source=="local-brief";
    public string SourceLabel=>IsPreferredSource?"本地词库":"ECDICT";
    public override string ToString()=>Word;
}
public sealed record ChineseSearchPage(string Query,IReadOnlyList<ChineseSearchResult> Items,int Total);

public sealed class ChineseReverseSearch(LocalDictionary dictionary,string? directory=null) : IDisposable
{
    private readonly string _directory=directory??Path.Combine(ApplicationPaths.RuntimeDirectory,"data");
    private readonly Dictionary<string,(long Offset,int Bytes)> _tokens=new(StringComparer.Ordinal);
    private Dictionary<string,SemanticEntry> _semantic=new(StringComparer.Ordinal);
    private FileStream? _postings;
    private readonly object _gate=new();
    private readonly object _loadGate=new();
    private Task? _loadTask;
    private PreferredIndex? _preferred;
    private readonly object _preferredGate = new();
    private sealed record PreferredIndex(LocalBriefDictionary Source,string[] Words,Dictionary<string,int[]> Tokens);
    private sealed class SemanticEntry {public int[] ids {get;set;}=[]; public string[] equivalents {get;set;}=[];}
    private sealed record QueryVariant(string Text,double Weight,bool Direct);
    private sealed record Sense(string Text,string Normalized,string Literal,bool Specialist,int Order,string Pos);
    public bool Available=>_postings!=null||dictionary.PreferredAvailable;
    private static readonly Regex Han=new(@"[\u3400-\u9fff]",RegexOptions.Compiled);
    private static readonly Regex Annotation=new(@"\[[^\]]*\]|\([^)]*\)|（[^）]*）",RegexOptions.Compiled);
    private static readonly Regex NegativePrefix=new(@"(?:不|没|无|非|未|欠|没有|缺乏|缺少|欠缺|不能|不太|难以|不善|不宜|不受)$",RegexOptions.Compiled);
    private static readonly string[][] AliasGroups=[
        ["快乐","开心","高兴","愉快","愉悦","喜悦","欢乐","快活"],
        ["健谈","爱说话","爱讲话","喜欢说话","多话","话多","善于交谈","饶舌","多嘴"],
        ["聪明","聪慧","聪颖","机智","伶俐"],
        ["美丽","漂亮","好看","俊美","秀丽"],
        ["生气","愤怒","发怒","恼怒","恼火","气愤"],
        ["勤奋","勤劳","勤勉","用功","刻苦"],
        ["安静","宁静","寂静","幽静","静谧"],
        ["准确","精确","精准","确切"],
        ["快速","迅速","飞快","快捷"],
        ["缓慢","慢速","迟缓"],
        ["重要","重大","要紧","至关重要"],
        ["困难","艰难","艰巨","棘手"],
        ["容易","简单","轻易"],
        ["诚实","诚恳","真诚","坦诚"],
        ["勇敢","英勇","无畏","胆大"],
        ["害怕","恐惧","畏惧","惧怕"],
        ["悲伤","伤心","难过","哀伤","忧伤"],
        ["疲惫","疲劳","疲倦","劳累"],
        ["巨大","庞大","硕大","极大"],
        ["微小","细小","渺小","极小"]];
    public static bool ContainsChinese(string text)=>Han.IsMatch(text);
    internal static string Normalize(string text)
    {
        var result=LiteralChinese(text);
        return result.Length>2&&result.EndsWith('的')?result[..^1]:result;
    }
    private static string LiteralChinese(string text)=>string.Concat(Han.Matches(text).Select(match=>match.Value));
    public Task LoadAsync()
    {
        lock(_loadGate)return _loadTask??=Task.Run(LoadCore);
    }
    private void LoadCore()
    {
        try{LoadFallback();}
        catch(Exception error)when(dictionary.PreferredAvailable&&error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _tokens.Clear();_semantic.Clear();
        }
        PreferredIndexFor(dictionary.PreferredDictionary);
    }
    private PreferredIndex? PreferredIndexFor(LocalBriefDictionary? source)
    {
        if (source is not {Available:true}) return null;
        lock (_preferredGate)
        {
            if (_preferred is { } current && ReferenceEquals(current.Source,source)) return current;
            var words=new List<string>();var postingsByToken=new Dictionary<string,List<int>>(StringComparer.Ordinal);
            foreach(var entry in source.EnumerateEntries())
            {
                var text=LiteralChinese(entry.translation);if(text.Length==0)continue;
                var id=words.Count;words.Add(entry.word);
                var grams=new HashSet<string>(StringComparer.Ordinal);
                for(var index=0;index<text.Length;index++)
                {
                    grams.Add("1:"+text[index]);
                    if(index+1<text.Length)grams.Add("2:"+text.Substring(index,2));
                }
                foreach(var gram in grams)
                {
                    if(!postingsByToken.TryGetValue(gram,out var ids))postingsByToken[gram]=ids=[];
                    ids.Add(id);
                }
            }
            var next=new PreferredIndex(source,words.ToArray(),postingsByToken.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToArray(),StringComparer.Ordinal));
            if (ReferenceEquals(source,dictionary.PreferredDictionary)) Volatile.Write(ref _preferred,next);
            return next;
        }
    }
    internal void InvalidatePreferredIndex() => Volatile.Write(ref _preferred,null);
    private void LoadFallback()
    {
        var path=Path.Combine(_directory,"chinese.postings");var tokens=Path.Combine(_directory,"chinese.tokens.tsv");
        var meta=Path.Combine(_directory,"chinese.meta.json");var synonyms=Path.Combine(_directory,"chinese.synonyms.json");
        if(!File.Exists(path)||!File.Exists(tokens)||!File.Exists(meta)||!File.Exists(synonyms))return;
        using var metadata=JsonDocument.Parse(File.ReadAllText(meta));
        using(var input=File.OpenRead(dictionary.IndexPath))
            if(!Convert.ToHexString(SHA256.HashData(input)).Equals(metadata.RootElement.GetProperty("dictionary_index_sha256").GetString(),StringComparison.OrdinalIgnoreCase))return;
        var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try
        {
            using var header=new BinaryReader(file,Encoding.UTF8,leaveOpen:true);
            if(Encoding.ASCII.GetString(header.ReadBytes(4))!="CJX1"||header.ReadInt32()!=dictionary.SourceRowCount)return;
            foreach(var line in File.ReadLines(tokens))
            {
                var parts=line.Split('\t');
                if(parts.Length==3&&long.TryParse(parts[1],CultureInfo.InvariantCulture,out var offset)&&int.TryParse(parts[2],out var bytes)&&offset>=8&&bytes>=0&&offset+bytes<=file.Length)
                    _tokens.TryAdd(parts[0],(offset,bytes));
            }
            _semantic=JsonSerializer.Deserialize<Dictionary<string,SemanticEntry>>(File.ReadAllText(synonyms))??[];
            _postings=file;file=null!;
        }
        finally{file?.Dispose();}
    }
    private int[] Posting(string token,CancellationToken cancellation)
    {
        if(_postings==null||!_tokens.TryGetValue(token,out var entry))return [];
        var bytes=new byte[entry.Bytes];lock(_gate){_postings.Position=entry.Offset;_postings.ReadExactly(bytes);}
        var ids=new List<int>();var last=0;var value=0;var shift=0;
        foreach(var item in bytes)
        {
            cancellation.ThrowIfCancellationRequested();value|=(item&127)<<shift;
            if((item&128)!=0){shift+=7;if(shift>28)throw new InvalidDataException("中文索引格式异常。");continue;}
            last+=value;ids.Add(last);value=shift=0;
        }
        return ids.ToArray();
    }
    private IReadOnlyList<QueryVariant> Variants(string query)
    {
        var variants=new Dictionary<string,QueryVariant>(StringComparer.Ordinal);
        void Add(string text,double weight,bool direct=false)
        {
            text=Normalize(text);if(text.Length is <1 or >40)return;
            if(!variants.TryGetValue(text,out var previous)||weight>previous.Weight)variants[text]=new(text,weight,direct);
        }
        Add(query,1,true);
        var shortened=query;
        foreach(var prefix in new[]{"我觉得","我感到","感觉很","心情很","感到","觉得","非常","十分","特别","很"})
            if(shortened.StartsWith(prefix,StringComparison.Ordinal)&&shortened.Length>prefix.Length+1)shortened=shortened[prefix.Length..];
        if(shortened!=query)Add(shortened,.94);
        foreach(var seed in variants.Values.ToArray())
        {
            foreach(var group in AliasGroups.Where(group=>group.Contains(seed.Text,StringComparer.Ordinal)))foreach(var alias in group)Add(alias,.97*seed.Weight);
            if(_semantic.TryGetValue(seed.Text,out var entry))foreach(var equivalent in entry.equivalents)Add(equivalent,.94*seed.Weight);
        }
        return variants.Values.OrderByDescending(value=>value.Weight).Take(40).ToArray();
    }
    private static Sense[] Senses(string translation)
    {
        var result=new List<Sense>();
        foreach(var line in translation.Split('\n'))
        {
            var specialist=line.TrimStart().StartsWith('[');
            var pos="";
            foreach(var segment in Regex.Split(Annotation.Replace(line,""),@"[,，;；]"))
            {
                var label=Regex.Match(segment,@"^\s*(n|a|adj|adv|v|vt|vi)\.");if(label.Success)pos=label.Groups[1].Value;
                var normalized=Normalize(segment);
                if(pos is "a" or "adj"&&normalized.Length>1&&normalized.EndsWith('的'))normalized=normalized[..^1];
                if(normalized.Length>0)result.Add(new(segment.Trim(),normalized,LiteralChinese(segment),specialist,result.Count,pos));
            }
        }
        return result.ToArray();
    }
    private static double MeaningScore(Sense sense,string query)
    {
        if(sense.Normalized==query)return 1100-(sense.Specialist?110:0)-Math.Min(180,sense.Order*19);
        foreach(var prefix in new[]{"感到","觉得","令人","让人","使人","非常","十分","能","能够","可","可以"})
            if(sense.Normalized==prefix+query)return 1075-(sense.Specialist?110:0)-Math.Min(180,sense.Order*19);
        if(query.Length==1)return 0;
        var position=sense.Normalized.IndexOf(query,StringComparison.Ordinal);
        if(position<0)return 0;
        if(position>0&&NegativePrefix.IsMatch(sense.Normalized[..position]))return 0;
        var ratio=(double)query.Length/sense.Normalized.Length;
        if(ratio<.25)return 0;
        return 590+ratio*330-(sense.Specialist?110:0)-Math.Min(180,sense.Order*19);
    }
    private static double Familiarity(DictionaryEntry entry)
    {
        var rank=new[]{entry.bnc,entry.frq}.Select(text=>int.TryParse(text,out var value)&&value>0?value:int.MaxValue).Min();
        var result=rank==int.MaxValue?0:Math.Max(0,105-Math.Log10(rank)*18);
        if(int.TryParse(entry.collins,out var stars))result+=Math.Min(stars,5)*8;
        if(entry.oxford=="1")result+=12;
        if(entry.tag.Split(' ').Any(tag=>tag is "cet4" or "cet6" or "ky"))result+=10;
        if(rank==int.MaxValue&&string.IsNullOrEmpty(entry.collins)&&entry.oxford!="1"&&string.IsNullOrEmpty(entry.tag))result-=70;
        if(entry.word.Any(char.IsUpper))result-=30;
        result-=entry.word.Count(character=>character==' ')*4;
        return result;
    }
    public Task<ChineseSearchPage> SearchAsync(string raw,int limit=40,CancellationToken cancellationToken=default)=>Task.Run(()=>
    {
        var query=Normalize(raw);
        var literalQuery=LiteralChinese(raw);
        if(query.Length is <1 or >40||raw.Length>100)throw new ArgumentException("请输入 1–40 个汉字的中文含义。");
        if(new[]{"的","了","和","是","一个","什么"}.Contains(query))return new ChineseSearchPage(raw,[],0);
        var source=dictionary.PreferredDictionary;
        var preferred=PreferredIndexFor(source);
        if(_postings==null&&preferred==null)throw new IOException("中文反查词库未就绪，请使用完整运行包。");
        var variants=Variants(query);var candidates=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        void AddSource(int id,double score=0)
        {
            if(id<0||id>=dictionary.SourceRowCount)return;
            var word=dictionary.SourceWord(id);
            if(!candidates.TryGetValue(word,out var previous)||score>previous)
            {
                candidates[word]=score;
            }
        }
        foreach(var variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grams=variant.Text.Length==1?new[]{"1:"+variant.Text}:Enumerable.Range(0,variant.Text.Length-1).Select(index=>"2:"+variant.Text.Substring(index,2)).Distinct().ToArray();
            if(preferred!=null)
            {
                var localPages=grams.Select(gram=>preferred.Tokens.GetValueOrDefault(gram)??[]).OrderBy(page=>page.Length).ToArray();
                if(localPages.Length>0&&localPages[0].Length>0)
                {
                    var ids=localPages[0].ToHashSet();foreach(var page in localPages.Skip(1)){ids.IntersectWith(page);if(ids.Count==0)break;}
                    foreach(var id in ids)candidates.TryAdd(preferred.Words[id],0);
                }
            }
            var pages=grams.Select(gram=>Posting(gram,cancellationToken)).OrderBy(page=>page.Length).ToArray();
            if(pages.Length>0&&pages[0].Length>0)
            {
                var ids=pages[0].ToHashSet();foreach(var page in pages.Skip(1)){ids.IntersectWith(page);if(ids.Count==0)break;}
                foreach(var id in ids)AddSource(id);
            }
            if(_semantic.TryGetValue(variant.Text,out var semantic))
                foreach(var id in semantic.ids)
                {
                    var score=1010*variant.Weight;
                    AddSource(id,score);
                }
        }
        var results=new List<ChineseSearchResult>();var bases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Candidate IDs are only word identities. Matching, ranking and the
            // displayed sense must all use the same preferred definition.
            var entry=dictionary.Lookup(pair.Key,source);if(entry==null)continue;
            var senses=Senses(entry.translation);var best=pair.Value;var bestMeaningScore=0d;var meaning="";var kind=best>0?"词网同义":"释义匹配";
            // An explicitly typed 的 constrains the complete sense. Without it,
            // keep conventional adjective matching (快乐 also matches 快乐的).
            var exactSense=senses.FirstOrDefault(sense=>sense.Literal==literalQuery||!literalQuery.EndsWith('的')&&sense.Normalized==query);
            var opposed=false;var positive=false;
            foreach(var variant in variants)
            foreach(var sense in senses)
            {
                var position=sense.Normalized.IndexOf(variant.Text,StringComparison.Ordinal);
                if(position>0&&NegativePrefix.IsMatch(sense.Normalized[..position]))opposed=true;
                var score=MeaningScore(sense,variant.Text)*variant.Weight;
                if(score>0&&sense.Pos is "a" or "adj" &&entry.HasEnglishDefinition&&!entry.HasAdjectiveDefinition)score-=90;
                if(score>0)positive=true;
                if(score>bestMeaningScore){bestMeaningScore=score;meaning=sense.Text;}
                if(score>best){best=score;meaning=sense.Text;kind=variant.Direct?"释义匹配":"近义表达";}
            }
            if(opposed&&!positive)continue;
            if(!positive&&best>0)best-=110;
            if(best<640&&exactSense==null)continue;
            if(exactSense!=null){meaning=exactSense.Text;kind="完全匹配";}
            if(meaning.Length==0)meaning=senses.FirstOrDefault()?.Text??entry.translation;
            var latin=Regex.Match(raw,@"[A-Za-z]{3,}").Value;
            var bonus=latin.Length>0&&entry.word.Contains(latin,StringComparison.OrdinalIgnoreCase)?50:0;
            var familiarity=Familiarity(entry);
            if(Regex.IsMatch(entry.translation,@"(?:^|\n)\s*(?:abbr\.|缩写)",RegexOptions.IgnoreCase))familiarity-=140;
            if(entry.HasHistoricalDefinition)familiarity-=100;
            var basis=entry.exchange.Split('/').FirstOrDefault(value=>value.StartsWith("0:",StringComparison.Ordinal));
            if(basis!=null)
            {
                var baseWord=basis[2..];var baseEntry=dictionary.Lookup(baseWord,source);
                var lexicalAdjective=entry.HasAdjectiveDefinition&&baseEntry?.HasAdjectiveDefinition!=true;
                if(!lexicalAdjective)bases[entry.word]=baseWord;
            }
            results.Add(new(entry.word,entry.translation,meaning,kind,best+familiarity+bonus){IsExactMatch=exactSense!=null,
                Source=entry.source=="local-brief"?"local-brief":"ecdict"});
        }
        var minimum=results.Count>0?Math.Max(720,results.Max(item=>item.Score)-260):720;
        var scores=results.GroupBy(item=>item.Word,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.Max(item=>item.Score),StringComparer.OrdinalIgnoreCase);
        var exactWords=results.Where(item=>item.IsExactMatch).Select(item=>item.Word).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Exact senses retain particles such as 的. Their tier cannot be outweighed
        // by familiarity or synonym scores. Keep the existing relevance filter;
        // collapse an exact inflection only when its base also matches exactly.
        var ranked=results.Where(item=>(item.IsExactMatch||item.Score>=minimum)&&(!bases.TryGetValue(item.Word,out var basis)||item.IsExactMatch&&!exactWords.Contains(basis)||basis.Equals(item.Word,StringComparison.OrdinalIgnoreCase)||!scores.TryGetValue(basis,out var baseScore)||baseScore<item.Score-60))
            .OrderByDescending(item=>item.IsExactMatch).ThenByDescending(item=>item.IsPreferredSource).ThenByDescending(item=>item.Score).ThenBy(item=>item.Word.Length).ThenBy(item=>item.Word,StringComparer.OrdinalIgnoreCase).DistinctBy(item=>item.Word,StringComparer.OrdinalIgnoreCase).ToArray();
        return new ChineseSearchPage(raw,ranked.Take(Math.Clamp(limit,1,120)).ToArray(),ranked.Length);
    },cancellationToken);
    public void Dispose()=>_postings?.Dispose();
}
