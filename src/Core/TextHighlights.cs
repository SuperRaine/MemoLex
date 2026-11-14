using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record HighlightPart(string Text,bool Highlighted);
public static class TextHighlights
{
    private static readonly HashSet<string> FunctionWords=new("a an the and or but of to in on at by with from for as is are was were be been being it its this that these those i you he she we they me him her us them my your his our their".Split(' '),StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VagueMeanings=new("事情 东西 情况 方面 部分 某事 某人 一种 其他 有关 相关 表示 进行 作为".Split(' '),StringComparer.Ordinal);
    public static IReadOnlySet<string> TranslationTerms(string english,string translation,string sourceWord,IReadOnlySet<string> matchedWords,Func<string,string?> lookup,IEnumerable<string>? explicitTerms=null,bool aligned=true)
    {
        var empty=new HashSet<string>(StringComparer.Ordinal);
        if(!aligned||string.IsNullOrWhiteSpace(english)||string.IsNullOrWhiteSpace(translation)||sourceWord.Any(char.IsWhiteSpace))return empty;
        var targets=new HashSet<string>(matchedWords,StringComparer.OrdinalIgnoreCase){sourceWord};
        var tokens=Regex.Matches(english,@"[A-Za-zÀ-ÖØ-öø-ÿ]+(?:['’-][A-Za-zÀ-ÖØ-öø-ÿ]+)*").Select(match=>match.Value).ToArray();
        var targetCount=tokens.Count(targets.Contains);
        if(targetCount==0)return empty;
        var candidates=new HashSet<string>(ChineseMeanings(lookup(sourceWord)),StringComparer.Ordinal);
        foreach(var word in tokens.Where(targets.Contains).Distinct(StringComparer.OrdinalIgnoreCase))candidates.UnionWith(ChineseMeanings(lookup(word)));
        if(explicitTerms!=null)foreach(var term in explicitTerms)
            if(Regex.IsMatch(term,@"^[\u3400-\u9fff]{2,12}$"))candidates.Add(term);
        candidates.RemoveWhere(term=>VagueMeanings.Contains(term)||!translation.Contains(term,StringComparison.Ordinal));
        if(candidates.Count==0)return empty;
        // Reject meanings that can instead belong to another word in this sentence.
        var competing=tokens.Where(word=>!targets.Contains(word)&&!FunctionWords.Contains(word)).Distinct(StringComparer.OrdinalIgnoreCase).Take(64)
            .SelectMany(word=>ChineseMeanings(lookup(word))).ToArray();
        candidates.RemoveWhere(term=>competing.Any(other=>other.Contains(term,StringComparison.Ordinal)||term.Contains(other,StringComparison.Ordinal)));
        // Several locations for one source occurrence have no reliable alignment.
        if(Split(translation,candidates).Count(part=>part.Highlighted)>targetCount)return empty;
        return candidates;
    }
    public static IReadOnlySet<string> ChineseMeanings(string? definition)
    {
        var words=new HashSet<string>(StringComparer.Ordinal);
        foreach(Match match in Regex.Matches(definition??"",@"[\u3400-\u9fff]{2,16}"))
        {
            var text=match.Value;
            if(text.Length is >=2 and <=12)words.Add(text);
            if(text.EndsWith('的')&&text.Length>=3)words.Add(text[..^1]);
        }
        return words;
    }
    public static IReadOnlyList<HighlightPart> Split(string text,IEnumerable<string> terms)
    {
        var values=terms.Where(value=>!string.IsNullOrWhiteSpace(value)).Distinct().OrderByDescending(value=>value.Length).ToArray();
        if(values.Length==0)return [new(text,false)];
        var output=new List<HighlightPart>();var position=0;
        foreach(Match match in Regex.Matches(text,string.Join("|",values.Select(Regex.Escape)),RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
        {
            if(match.Index>position)output.Add(new(text[position..match.Index],false));
            output.Add(new(match.Value,true));position=match.Index+match.Length;
        }
        if(position<text.Length)output.Add(new(text[position..],false));
        return output;
    }
}
