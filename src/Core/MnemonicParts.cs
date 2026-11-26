using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record MnemonicPart(
    string Form,
    string Meaning,
    string Role = "词根",
    IReadOnlyList<string>? Examples = null,
    string? FocusExplanation = null);

public sealed record MnemonicBreakdown(
    IReadOnlyList<MnemonicPart> Parts,
    string Explanation,
    string? Word);

public sealed record MnemonicAnalysis(
    IReadOnlyList<MnemonicBreakdown> Breakdowns,
    string RemainingText);

public static partial class MnemonicParts
{
    // Match authored constructions such as re-（再次）＋view（看）→回头再看,
    // including stem changes such as abbreviat(e) or accompan(y→i).
    [GeneratedRegex(@"^(?<form>-?[a-z]+(?:\([a-z]+(?:→[a-z]+)?\))?[a-z]*-?)(?:\s*(?:（(?<meaning>.+)）|\((?<meaning>.+)\))|(?:\s+|\s*[：:]\s*)(?<meaning>[\p{IsCJKUnifiedIdeographs}].*))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabeledPart();

    [GeneratedRegex(@"^-?[a-z]+(?:\([a-z]+(?:→[a-z]+)?\))?[a-z]*-?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnlabeledPart();

    public static string NormalizeFormKey(string form)
    {
        var trimmed = form.Trim().ToLowerInvariant();
        var cleaned = Regex.Replace(trimmed, @"\([^)]*\)", "");
        return cleaned.Length > 0 ? cleaned : trimmed;
    }

    public static string ClassifyRole(string form, int index, int totalCount)
    {
        var trimmed = form.Trim();
        if (trimmed.EndsWith('-') && !trimmed.StartsWith('-')) return "前缀";
        if (trimmed.StartsWith('-') && !trimmed.EndsWith('-')) return "后缀";
        if (trimmed.StartsWith('-') && trimmed.EndsWith('-')) return "词缀";
        if (index == 0 && totalCount >= 2 && trimmed.Length <= 3 && !trimmed.Contains(' ')) return "词根";
        return "词根";
    }

    public static string BuildFocusExplanation(string form, string meaning, string role, IReadOnlyList<string>? examples)
    {
        var cleanMeaning = meaning.Trim().TrimEnd('。', '；', ';');
        var baseText = $"{role} {form} 表示「{cleanMeaning}」";
        if (examples is { Count: > 0 })
            return $"{baseText}，常见于 {string.Join("、", examples)}。";
        return $"{baseText}。";
    }

    public static IReadOnlyList<MnemonicBreakdown> Parse(string word, string? type, string text,
        Func<string, string, string, IReadOnlyList<string>>? findExamples = null) =>
        Analyze(word, type, text, findExamples).Breakdowns;

    public static MnemonicAnalysis Analyze(string word, string? type, string text,
        Func<string, string, string, IReadOnlyList<string>>? findExamples = null)
    {
        if (MnemonicTypes.Normalize(type) is not ("词根词缀" or "派生"))
            return new([], text);

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var breakdowns = new List<MnemonicBreakdown>();
        var remaining = new List<string>();

        foreach (var line in lines)
        {
            if (breakdowns.Count < 3 && TryParseLine(word, line, breakdowns.Count == 0, findExamples, out var breakdown))
            {
                breakdowns.Add(breakdown);
                continue;
            }
            remaining.Add(line);
        }

        if (breakdowns.Count == 0)
            return new([], text);

        return new(breakdowns, string.Join("\n", remaining));
    }

    private static bool TryParseLine(
        string word,
        string line,
        bool isPrimary,
        Func<string, string, string, IReadOnlyList<string>>? findExamples,
        out MnemonicBreakdown breakdown)
    {
        breakdown = default!;
        var arrow = line.IndexOf('→');
        var equals = line.IndexOfAny(['=', '＝']);
        var boundary = arrow < 0 ? equals : equals < 0 ? arrow : Math.Min(arrow, equals);
        if (boundary < 0) return false;

        var left = line[..boundary];
        // Ignore paren-internal arrows like accompan(y→i) when locating the formula arrow.
        while (boundary >= 0 && IsInsideParentheses(line, boundary))
        {
            var nextArrow = line.IndexOf('→', boundary + 1);
            var nextEquals = line.IndexOfAny(['=', '＝'], boundary + 1);
            boundary = nextArrow < 0 ? nextEquals : nextEquals < 0 ? nextArrow : Math.Min(nextArrow, nextEquals);
        }
        if (boundary < 0) return false;
        left = line[..boundary];

        var pieces = left.Split(['+', '＋'], StringSplitOptions.TrimEntries);
        if (pieces.Length is < 2 or > 8) return false;

        var rawParts = new List<(string Form, string Meaning)>(pieces.Length);
        foreach (var piece in pieces)
        {
            var match = LabeledPart().Match(piece);
            if (match.Success && match.Groups["meaning"].Value.Trim() is { Length: > 0 } meaning)
                rawParts.Add((match.Groups["form"].Value, meaning));
            else if (UnlabeledPart().IsMatch(piece))
                rawParts.Add((piece, ""));
            else
                return false;
        }

        if (rawParts.Count(part => part.Meaning.Length > 0) < 2) return false;
        var explanation = line[(boundary + 1)..].Trim();
        if (explanation.Length == 0) return false;

        var parts = new List<MnemonicPart>(rawParts.Count);
        for (var i = 0; i < rawParts.Count; i++)
        {
            var (form, meaning) = rawParts[i];
            var role = ClassifyRole(form, i, rawParts.Count);
            var examples = meaning.Length > 0 && findExamples != null
                ? findExamples(form, meaning, word)
                : [];
            var focus = meaning.Length > 0
                ? BuildFocusExplanation(form, meaning, role, examples)
                : null;
            parts.Add(new(form, meaning, role, examples, focus));
        }

        var resultWord = ResolveResultWord(word, parts, isPrimary);
        breakdown = new(parts, explanation, resultWord);
        return true;
    }

    private static bool IsInsideParentheses(string text, int index)
    {
        var depth = 0;
        for (var i = 0; i < index; i++)
        {
            if (text[i] is '(' or '（') depth++;
            else if (text[i] is ')' or '）' && depth > 0) depth--;
        }
        return depth > 0;
    }

    private static string? ResolveResultWord(string entryWord, IReadOnlyList<MnemonicPart> parts, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(entryWord)) return null;
        var rawCombined = string.Concat(parts.Select(p => ApplyParen(p.Form, keepParen: true)));
        var droppedCombined = string.Concat(parts.Select(p => ApplyParen(p.Form, keepParen: false)));
        if (MatchesWord(entryWord, rawCombined) || MatchesWord(entryWord, droppedCombined))
            return entryWord;

        // Handle silent-e drop when joining stem + vowel-initial suffix (e.g. abdicate + -ion -> abdication).
        var joined = CombineWithSilentEDrop(parts);
        if (MatchesWord(entryWord, joined))
            return entryWord;

        if (isPrimary && entryWord.Length >= 4 && EditDistance(entryWord.ToLowerInvariant(), droppedCombined.ToLowerInvariant()) <= 2)
            return entryWord;

        return isPrimary ? entryWord : null;
    }

    private static string ApplyParen(string form, bool keepParen)
    {
        var value = form.Replace("-", "");
        if (value.Contains('→'))
        {
            return Regex.Replace(value, @"\([a-z]+→([a-z]+)\)", "$1", RegexOptions.IgnoreCase);
        }
        return keepParen
            ? value.Replace("(", "").Replace(")", "")
            : Regex.Replace(value, @"\([^)]*\)", "");
    }

    private static string CombineWithSilentEDrop(IReadOnlyList<MnemonicPart> parts)
    {
        var result = "";
        foreach (var part in parts)
        {
            var piece = ApplyParen(part.Form, keepParen: false);
            if (result.EndsWith('e') && piece.Length > 0 && "aeiouy".Contains(char.ToLowerInvariant(piece[0])))
                result = result[..^1];
            result += piece;
        }
        return result;
    }

    private static bool MatchesWord(string expected, string candidate) =>
        candidate.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static int EditDistance(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 2) return 3;
        var prev = new int[right.Length + 1];
        var curr = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) prev[j] = j;
        for (var i = 1; i <= left.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[right.Length];
    }
}
