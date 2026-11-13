using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record HighlightPart(string Text,bool Highlighted);
public static class TextHighlights
{
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
