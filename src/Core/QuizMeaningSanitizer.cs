using System.Text.RegularExpressions;

namespace MemoLex.Core;

public static partial class QuizMeaningSanitizer
{
    private static readonly HashSet<string> SafeAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "n", "v", "vt", "vi", "adj", "adv", "prep", "conj", "pron", "num", "art",
        "int", "interj", "aux", "abbr", "pl", "pp", "sb", "sth", "etc", "vs", "cf",
        "eg", "ie", "uk", "us", "am", "br", "a", "ad", "na", "un", "phr"
    };

    private static readonly string[] CommonSuffixes =
    [
        "ational", "ization", "isation", "fulness", "ousness", "iveness", "ability", "ibility",
        "ically", "ations", "action", "ements", "ments", "tions", "sions", "ances", "ences",
        "ating", "izing", "ising", "ingly", "edly", "ness", "ment", "tion", "sion", "ance", "ence",
        "able", "ible", "less", "ious", "eous", "ical", "ally", "ship", "hood", "ward",
        "ing", "ied", "ies", "est", "ful", "ive", "ous", "ize", "ise", "ist", "ism", "ity", "ary", "ory",
        "ed", "er", "es", "ly", "al", "ic", "en", "s"
    ];

    [GeneratedRegex(@"[（(\[【][^（）()\[\]【】]*[）)\]】]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedPattern();

    [GeneratedRegex(@"[A-Za-z][A-Za-z'\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex LatinTokenPattern();

    [GeneratedRegex(@"(?:分词|过去式|过去时|复数|比较级|最高级|单数|三单|变形|变体|派生|原形|原型|形式|等于|相当于|亦作|简称|缩写|词根|词缀|词尾|同[A-Za-z\s]|见[A-Za-z\s]|[=＝])", RegexOptions.CultureInvariant)]
    private static partial Regex GrammarNoteKeywordPattern();

    [GeneratedRegex(@"(?:——\s*见|见|等于|相当于|亦作|同|[=＝])\s*[A-Za-z][A-Za-z'\-\s]*", RegexOptions.CultureInvariant)]
    private static partial Regex InlineReferencePattern();

    [GeneratedRegex(@"[A-Za-z][A-Za-z'\-]*\s*的?\s*(?:现在分词|过去分词|过去式(?:和|与)过去分词|过去式|过去时|名词复数|复数形式|复数|第三人称(?:单数|-[A-Za-z]+形式)|三单形式|比较级|最高级|变形|变体|派生词|原形|原型|(?:动词|名词|形容词|副词)?形式)(?:形式)?", RegexOptions.CultureInvariant)]
    private static partial Regex InlineInflectionPattern();

    [GeneratedRegex(@"^((?:adj|adv|prep|pron|conj|interj|int|n|v|vt|vi|aux|art|det|num|part|ptcp|contr|a|ad|s|abbr|pref|suf|comb|vbl|phr|un|na|pl|pp)\.(?:(?:\s*[/&,]\s*|\s+)(?:adj|adv|prep|pron|conj|interj|int|n|v|vt|vi|aux|art|det|num|part|ptcp|contr|a|ad|s|abbr|pref|suf|comb|vbl|phr|un|na|pl|pp)\.)*)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingPosPattern();

    [GeneratedRegex(@"[\u4E00-\u9FFF]", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseCharPattern();

    public static string Sanitize(string? rawMeaning, string targetWord, string? exchange = null)
    {
        if (string.IsNullOrWhiteSpace(rawMeaning)) return "";

        var cleanTarget = (targetWord ?? "").Trim().ToLowerInvariant();
        var relatedForms = CollectRelatedForms(cleanTarget, exchange);
        var targetStem = ExtractStem(cleanTarget);

        var lines = rawMeaning
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace(@"\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var keptLines = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            var posMatch = LeadingPosPattern().Match(rawLine);
            var posPrefix = posMatch.Success ? posMatch.Groups[1].Value.Trim() : "";
            var body = posMatch.Success ? rawLine[posMatch.Length..] : rawLine;

            body = StripBracketedLeaks(body, cleanTarget, relatedForms, targetStem);

            var clauses = body.Split(['；', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var cleanClauses = new List<string>(clauses.Length);
            foreach (var rawClause in clauses)
            {
                var clause = InlineInflectionPattern().Replace(rawClause, "");
                clause = InlineReferencePattern().Replace(clause, "");
                clause = LatinTokenPattern().Replace(clause, m =>
                    IsLeakingWord(m.Value, cleanTarget, relatedForms, targetStem) ? "" : m.Value);
                clause = Regex.Replace(clause, @"[（(\[【]\s*[，,、；;：:\s]*[）)\]】]", "");
                clause = Regex.Replace(clause, @"\s{2,}", " ");
                clause = Regex.Replace(clause, @"([，,、])\s*([，,、])+", "$1");
                clause = clause.Trim(' ', '\t', '，', ',', '、', '；', ';', '：', ':', '=', '＝', '-', '—');
                if (clause.Length > 0)
                    cleanClauses.Add(clause);
            }

            if (cleanClauses.Count == 0) continue;
            var rebuiltBody = string.Join("；", cleanClauses);
            keptLines.Add(posPrefix.Length > 0 ? $"{posPrefix} {rebuiltBody}" : rebuiltBody);
        }

        return string.Join("\n", keptLines);
    }

    private static string StripBracketedLeaks(string text, string cleanTarget, HashSet<string> relatedForms, string targetStem)
    {
        return BracketedPattern().Replace(text, match =>
        {
            var full = match.Value;
            if (full.Length < 2) return full;
            var open = full[0];
            var close = full[^1];
            var inner = full[1..^1].Trim();
            if (!LatinTokenPattern().IsMatch(inner)) return full;

            var hasLeakingToken = false;
            var hasNonAbbrLatin = false;
            foreach (Match token in LatinTokenPattern().Matches(inner))
            {
                if (SafeAbbreviations.Contains(token.Value)) continue;
                hasNonAbbrLatin = true;
                if (IsLeakingWord(token.Value, cleanTarget, relatedForms, targetStem))
                {
                    hasLeakingToken = true;
                    break;
                }
            }
            if (!hasNonAbbrLatin) return full;

            var isGrammarNote = GrammarNoteKeywordPattern().IsMatch(inner);
            var hasChinese = ChineseCharPattern().IsMatch(inner);
            if (!hasLeakingToken && !isGrammarNote && hasChinese)
                return full;

            // Keep any non-leaking Chinese prefix inside mixed notes like "（男子名，等于bettor）" -> "（男子名）".
            if (hasChinese)
            {
                var subParts = inner.Split(['，', ',', '；', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var safeParts = new List<string>();
                foreach (var sub in subParts)
                {
                    if (LatinTokenPattern().IsMatch(sub) && !sub.Split(' ').All(SafeAbbreviations.Contains))
                        continue;
                    if (GrammarNoteKeywordPattern().IsMatch(sub))
                        continue;
                    safeParts.Add(sub);
                }
                if (safeParts.Count > 0)
                    return $"{open}{string.Join("，", safeParts)}{close}";
            }

            return "";
        });
    }

    private static HashSet<string> CollectRelatedForms(string cleanTarget, string? exchange)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cleanTarget.Length > 0)
        {
            forms.Add(cleanTarget);
            foreach (var part in cleanTarget.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries))
                if (part.Length >= 2) forms.Add(part);
        }
        if (!string.IsNullOrWhiteSpace(exchange))
        {
            foreach (var segment in exchange.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = segment.IndexOf(':');
                if (colon <= 0 || colon >= segment.Length - 1) continue;
                var tag = segment[..colon];
                if (tag == "1") continue; // "1:dp" / "1:i" are inflection type codes, not words
                var form = segment[(colon + 1)..].Trim();
                if (form.Length >= 2) forms.Add(form);
            }
        }
        return forms;
    }

    private static bool IsLeakingWord(string token, string cleanTarget, HashSet<string> relatedForms, string targetStem)
    {
        var lower = token.Trim().ToLowerInvariant();
        if (lower.Length < 2 || SafeAbbreviations.Contains(lower)) return false;
        if (relatedForms.Contains(lower)) return true;

        var tokenStem = ExtractStem(lower);
        foreach (var related in relatedForms)
        {
            if (related.Length < 2) continue;
            if (lower.Equals(related, StringComparison.OrdinalIgnoreCase)) return true;
            var relStem = ExtractStem(related);
            if (relStem.Length >= 3 && tokenStem.Length >= 3 && relStem.Equals(tokenStem, StringComparison.OrdinalIgnoreCase))
                return true;
            var minPrefix = Math.Min(lower.Length, related.Length) <= 4 ? 3 : 4;
            if (CommonPrefix(lower, related) >= minPrefix
                && (lower.StartsWith(relStem, StringComparison.OrdinalIgnoreCase)
                    || related.StartsWith(tokenStem, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (targetStem.Length >= 3 && tokenStem.Length >= 3 && targetStem.Equals(tokenStem, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string ExtractStem(string word)
    {
        var w = word.Trim().ToLowerInvariant();
        if (w.Length <= 3) return w;
        foreach (var suffix in CommonSuffixes)
        {
            if (w.Length > suffix.Length + 2 && w.EndsWith(suffix, StringComparison.Ordinal))
            {
                w = w[..^suffix.Length];
                break;
            }
        }
        // Collapse doubled trailing consonant (e.g., bigg -> big, runn -> run)
        if (w.Length >= 4 && w[^1] == w[^2]) w = w[..^1];
        // Drop silent trailing 'e' so abide (abid) matches abiding (abid)
        if (w.Length >= 4 && w[^1] == 'e') w = w[..^1];
        return w;
    }

    private static int CommonPrefix(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < max && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
        return i;
    }
}
