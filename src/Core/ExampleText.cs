using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record SentencePart(string Text, string? LookupWord)
{
    public bool IsWord => LookupWord != null;
}

public static class ExampleText
{
    private static readonly Regex WordPattern = new(@"[A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+(?:['’][A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+)*", RegexOptions.Compiled);
    public static string CleanMarkup(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?[A-Za-z][^>]*>", "");
        text = WebUtility.HtmlDecode(text).Replace("***", "").Replace("**", "");
        text = Regex.Replace(text, @"\|\d+\|", "____");
        text = Regex.Replace(text, @"(?:\s*//)+\s*$", "");
        return text.Replace("\r\n", "\n").Trim();
    }
    public static IReadOnlyList<SentencePart> Tokenize(string text)
    {
        var parts = new List<SentencePart>();
        var position = 0;
        foreach (Match match in WordPattern.Matches(text))
        {
            if (match.Index > position) parts.Add(new SentencePart(text[position..match.Index], null));
            parts.Add(new SentencePart(match.Value, NormalizeLookup(match.Value)));
            position = match.Index + match.Length;
        }
        if (position < text.Length) parts.Add(new SentencePart(text[position..], null));
        return parts;
    }
    public static string NormalizeLookup(string word)
    {
        var normalized = new StringBuilder();
        foreach (var character in word.Replace('’', '\'').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) normalized.Append(character);
        return normalized.ToString().Normalize(NormalizationForm.FormC);
    }
    public static string ResolveLookupWord(string word, Func<string, bool> exists)
    {
        word = NormalizeLookup(word);
        if (exists(word)) return word;
        if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) && word.Length > 2 && exists(word[..^2])) return word[..^2];
        return word;
    }
    public static IReadOnlySet<string> HighlightedWords(string markup)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(markup, @"<span\b[^>]*>(.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            foreach (var part in Tokenize(CleanMarkup(match.Groups[1].Value)))
                if (part.LookupWord != null) words.Add(part.LookupWord);
        return words;
    }
    public static IReadOnlySet<string> HighlightedPhrases(string markup) => Regex.Matches(markup,@"<span\b[^>]*>(.*?)</span>",RegexOptions.IgnoreCase|RegexOptions.Singleline)
        .Select(match=>CleanMarkup(match.Groups[1].Value)).Where(value=>value.Length>0).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
