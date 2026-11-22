using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record NotepadDisplayRow(string Word,string Meaning,bool IsChapter=false);
public static class NotepadContent
{
    public static string NormalizeContent(string input)
    {
        var newline=input.Contains("\r\n",StringComparison.Ordinal)?"\r\n":input.Contains('\r')&&!input.Contains('\n')?"\r":"\n";
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var lines=new List<string>();
        foreach(var raw in Regex.Split(input,@"\r\n|\r|\n"))
        {
            var line=raw.Trim();
            if(line.Length==0||line.StartsWith('#')){lines.Add(line);continue;}
            foreach(var word in line.Split(new[]{',','，',';','；'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
                if(seen.Add(word))lines.Add(word);
        }
        return string.Join(newline,lines);
    }
    public static IReadOnlyList<string> ParseWords(string input)
    {
        var words=input.Split(new[]{'\r','\n',',','，',';','；'},StringSplitOptions.RemoveEmptyEntries).Select(word=>word.Trim()).Where(word=>word.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(words.Length==0)throw new ArgumentException("请至少输入一个英文单词或短语。");
        var invalid=words.FirstOrDefault(word=>word.Length>100||!Regex.IsMatch(word,@"^[a-zA-Z][a-zA-Z .’'\-]*$"));
        if(invalid!=null)throw new ArgumentException("每行填写一个英文单词或短语，多个也可用逗号分隔；请检查："+invalid[..Math.Min(invalid.Length,30)]);
        return words;
    }
    public static string AddWords(string content,IEnumerable<string> words)
    {
        var existing=Words(content).ToHashSet(StringComparer.OrdinalIgnoreCase);var added=words.Where(word=>existing.Add(word)).ToArray();
        if(added.Length==0)return content;
        var newline=content.Contains("\r\n",StringComparison.Ordinal)?"\r\n":content.Contains('\r')&&!content.Contains('\n')?"\r":"\n";
        return content+(content.Length>0&&content[^1] is not '\r' and not '\n'?newline:"")+string.Join(newline,added);
    }
    public static string RemoveWords(string content,IEnumerable<string> words)
    {
        var removed=words.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Regex.Replace(content,@"[^\r\n]+(?:\r\n|\r|\n|$)",match=>
        {
            var line=match.Value.TrimEnd('\r','\n').Trim();
            return !line.StartsWith('#')&&removed.Contains(line)?"":match.Value;
        });
    }
    public static IReadOnlyList<string> Words(string content)=>content.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
        .Select(line=>line.Trim()).Where(line=>line.Length>0&&!line.StartsWith('#')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static IReadOnlyList<NotepadDisplayRow> Describe(string content,Func<string,string?> meaning,CancellationToken cancellationToken=default)
    {
        var rows=new List<NotepadDisplayRow>();
        foreach(var raw in content.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();var line=raw.Trim();if(line.Length==0)continue;
            if(line.StartsWith('#'))rows.Add(new(line.TrimStart('#').Trim(),"",true));
            else
            {
                var translation=DictionaryEntry.FormatDisplayMeaning(meaning(line));
                rows.Add(new(line,translation));
            }
        }
        return rows;
    }
}
