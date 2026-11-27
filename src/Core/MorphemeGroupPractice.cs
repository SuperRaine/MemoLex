using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record MorphemeSegment(string Form, string Meaning, bool IsTargetRoot);

public sealed record MorphemePracticeMember(
    string Word,
    string Meaning,
    string PromptMeaning,
    string Explanation,
    IReadOnlyList<MorphemeSegment> Segments,
    bool IsUserWord);

public sealed record MorphemePracticeGroup(
    string RootForm,
    string RootMeaning,
    IReadOnlyList<MorphemePracticeMember> Members);

public static partial class MorphemeGroupPractice
{
    [GeneratedRegex(@"\b(?:n|v|vt|vi|adj|adv|prep|conj|pron|num|art|interj|abbr)\.\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PosTagPattern();

    [GeneratedRegex(@"[a-zA-Z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex LatinWordPattern();

    public static MorphemePracticeGroup? Build(
        MnemonicStore store,
        string rootForm,
        string rootMeaning,
        string currentWord,
        Func<string, string?> lookupMeaning,
        IReadOnlySet<string>? priorityWords = null,
        int maxMembers = 6)
    {
        var members = store.FindMorphemePracticeMembers(
            rootForm,
            rootMeaning,
            currentWord,
            lookupMeaning,
            priorityWords,
            Math.Clamp(maxMembers, 2, 8));
        if (members.Count < 2) return null;
        return new MorphemePracticeGroup(rootForm.Trim(), rootMeaning.Trim(), members);
    }

    public static string BuildPromptMeaning(string rawMeaning, string fallbackExplanation, string word)
    {
        var candidate = CleanForPrompt(rawMeaning, word);
        if (candidate.Length >= 2) return candidate;
        var fromExp = CleanForPrompt(fallbackExplanation, word);
        if (fromExp.Length >= 2) return fromExp;
        return rawMeaning.Trim();
    }

    private static string CleanForPrompt(string? text, string word)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sanitized = QuizMeaningSanitizer.Sanitize(text, word);
        if (string.IsNullOrWhiteSpace(sanitized)) return "";
        var lines = sanitized.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = lines.FirstOrDefault() ?? "";
        // Remove POS prefixes like "v. " and any remaining English leakage.
        chosen = PosTagPattern().Replace(chosen, "").Trim();
        chosen = LatinWordPattern().Replace(chosen, match =>
            match.Value.Equals(word, StringComparison.OrdinalIgnoreCase) ? "" : match.Value).Trim();
        chosen = Regex.Replace(chosen, @"[（(]\s*[)）]", "").Trim('；', ';', '，', ',', '、', ' ');
        // Keep up to 2 concise clauses so the question reads naturally like "运出；出口"
        var clauses = chosen.Split(['；', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var concise = string.Join("；", clauses.Take(2));
        if (concise.Length > 34) concise = concise[..34].TrimEnd('，', '、', ' ') + "…";
        return concise;
    }
}
