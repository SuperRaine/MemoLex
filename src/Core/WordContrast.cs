using System.Text;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record LetterSpan(string Text, bool Highlight);

public sealed record WordContrastEntry(
    string Word,
    string Phonetic,
    string PrimaryPos,
    string[] AllPos,
    string Meaning,
    string CoreSense,
    string[] SharedSenses,
    string[] TargetUniqueSenses,
    string[] CandidateUniqueSenses,
    string PosSummary,
    string Verdict,
    string SpellingDiffSummary,
    LetterSpan[] TargetSpans,
    LetterSpan[] CandidateSpans,
    string[] TargetPhrases,
    string[] CandidatePhrases,
    string? CandidateMnemonic);

public sealed record WordContrastReport(
    string Mode,
    string TargetWord,
    string TargetPhonetic,
    string TargetPrimaryPos,
    string TargetMeaning,
    string TargetCoreSense,
    string? TargetMnemonic,
    string[] TargetPhrases,
    IReadOnlyList<WordContrastEntry> Items);

public static partial class WordContrast
{
    [GeneratedRegex(@"[\u4E00-\u9FFF]", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseCharPattern();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|（[^）]*）", RegexOptions.CultureInvariant)]
    private static partial Regex AnnotationPattern();

    public static async Task<WordContrastReport?> BuildAsync(
        string word,
        string mode,
        LocalDictionary dictionary,
        RelatedWords relatedWords,
        MnemonicStore? mnemonics = null,
        string? accountKey = null,
        string? focusWord = null,
        CancellationToken cancellationToken = default)
    {
        var cleanWord = (word ?? "").Trim();
        if (cleanWord.Length == 0) return null;

        var normalizedMode = string.Equals(mode, "similar", StringComparison.OrdinalIgnoreCase) ? "similar" : "synonym";
        var relations = await relatedWords.LookupAsync(cleanWord, cancellationToken);
        var targetEntry = dictionary.Lookup(cleanWord);
        var targetMeaning = targetEntry?.DisplayMeaning ?? "";
        var targetPhonetic = FormatPhonetic(targetEntry?.phonetic);
        var targetPrimaryPos = ExtractPrimaryPos(targetMeaning);
        var targetPosGroup = NormalizePosBucket(targetPrimaryPos);
        var targetAllPos = ExtractAllPosLabels(targetMeaning);
        var targetAllPosGroups = ExtractAllPosGroups(targetMeaning);
        var targetCore = BriefPrimarySense(targetMeaning);
        var targetSenses = ExtractSenseTokens(targetMeaning);
        var targetMnemonic = ReadMnemonicSummary(mnemonics, cleanWord, accountKey);
        var targetPhrases = relatedWords.LookupPhrases(cleanWord, cancellationToken)
            .Select(p => p.Word)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        var sourceList = normalizedMode == "similar"
            ? relations.Similar.ToList()
            : relations.Synonyms.ToList();

        if (!string.IsNullOrWhiteSpace(focusWord))
        {
            var idx = sourceList.FindIndex(x => x.Word.Equals(focusWord.Trim(), StringComparison.OrdinalIgnoreCase));
            if (idx > 0)
            {
                var focused = sourceList[idx];
                sourceList.RemoveAt(idx);
                sourceList.Insert(0, focused);
            }
            else if (idx < 0 && !focusWord.Trim().Equals(cleanWord, StringComparison.OrdinalIgnoreCase))
            {
                var focusEntry = dictionary.Lookup(focusWord.Trim());
                if (focusEntry != null && !string.IsNullOrWhiteSpace(focusEntry.DisplayMeaning))
                    sourceList.Insert(0, new RelatedWord(focusEntry.word, focusEntry.DisplayMeaning));
            }
        }

        if (sourceList.Count == 0)
        {
            return new WordContrastReport(
                normalizedMode,
                targetEntry?.word ?? cleanWord,
                targetPhonetic,
                targetPrimaryPos,
                CompactMeaning(targetMeaning),
                targetCore,
                targetMnemonic,
                targetPhrases,
                []);
        }

        var items = new List<WordContrastEntry>(Math.Min(10, sourceList.Count));
        foreach (var rel in sourceList.Take(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = dictionary.Lookup(rel.Word);
            var candWord = entry?.word ?? rel.Word;
            var candMeaning = !string.IsNullOrWhiteSpace(entry?.DisplayMeaning) ? entry.DisplayMeaning : rel.Meaning;
            var candPhonetic = FormatPhonetic(entry?.phonetic);
            var candPrimaryPos = ExtractPrimaryPos(candMeaning);
            var candPosGroup = NormalizePosBucket(candPrimaryPos);
            var candAllPos = ExtractAllPosLabels(candMeaning);
            var candAllPosGroups = ExtractAllPosGroups(candMeaning);
            var candCore = BriefPrimarySense(candMeaning);
            var candSenses = ExtractSenseTokens(candMeaning);

            var shared = FindSharedSenses(targetSenses, candSenses);
            var targetUnique = targetSenses.Where(s => !shared.Contains(s)).Take(3).ToArray();
            var candUnique = candSenses.Where(s => !shared.Contains(s)).Take(3).ToArray();

            var (targetSpans, candSpans, spellingDiff) = BuildSpellingComparison(targetEntry?.word ?? cleanWord, candWord);
            var posSummary = BuildPosSummary(targetEntry?.word ?? cleanWord, targetPrimaryPos, targetPosGroup, targetAllPosGroups, candWord, candPrimaryPos, candPosGroup, candAllPosGroups);
            var candPhrases = normalizedMode == "synonym"
                ? relatedWords.LookupPhrases(candWord, cancellationToken).Select(p => p.Word).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray()
                : [];
            var candMnemonic = ReadMnemonicSummary(mnemonics, candWord, accountKey);

            var verdict = normalizedMode == "similar"
                ? BuildSimilarVerdict(targetEntry?.word ?? cleanWord, targetPrimaryPos, targetPosGroup, targetCore, candWord, candPrimaryPos, candPosGroup, candCore, shared, spellingDiff, targetPhonetic, candPhonetic)
                : BuildSynonymVerdict(targetEntry?.word ?? cleanWord, targetPrimaryPos, targetPosGroup, targetAllPosGroups, targetCore, targetUnique, candWord, candPrimaryPos, candPosGroup, candAllPosGroups, candCore, candUnique, shared);

            items.Add(new WordContrastEntry(
                candWord,
                candPhonetic,
                candPrimaryPos,
                candAllPos,
                CompactMeaning(candMeaning),
                candCore,
                shared.Take(3).ToArray(),
                targetUnique,
                candUnique,
                posSummary,
                verdict,
                spellingDiff,
                targetSpans,
                candSpans,
                targetPhrases,
                candPhrases,
                candMnemonic));
        }

        return new WordContrastReport(
            normalizedMode,
            targetEntry?.word ?? cleanWord,
            targetPhonetic,
            targetPrimaryPos,
            CompactMeaning(targetMeaning),
            targetCore,
            targetMnemonic,
            targetPhrases,
            items);
    }

    private static string BuildPosSummary(
        string targetWord, string targetPos, string targetGroup, HashSet<string> targetAll,
        string candWord, string candPos, string candGroup, HashSet<string> candAll)
    {
        if (targetGroup.Length == 0 || candGroup.Length == 0) return "";
        var tName = PosName(targetGroup);
        var cName = PosName(candGroup);
        if (targetGroup == candGroup)
            return $"同为{tName}（{targetPos}）";
        if (candAll.Contains(targetGroup) || targetAll.Contains(candGroup))
            return $"主词性不同：{targetWord} 主作{tName}，{candWord} 主作{cName}（词性有交叉）";
        return $"词性不同：{tName}（{targetPos}） vs {cName}（{candPos}）";
    }

    private static string BuildSynonymVerdict(
        string targetWord, string targetPos, string targetGroup, HashSet<string> targetAll, string targetCore, string[] targetUnique,
        string candWord, string candPos, string candGroup, HashSet<string> candAll, string candCore, string[] candUnique,
        List<string> shared)
    {
        var tName = PosName(targetGroup);
        var cName = PosName(candGroup);
        var sharedStr = string.Join("、", shared.Take(2));
        var strictPosDiff = targetGroup.Length > 0 && candGroup.Length > 0 && !candAll.Contains(targetGroup);

        if (strictPosDiff)
        {
            var commonPart = shared.Count > 0 ? $"两者都含「{sharedStr}」之意，但词性不同：" : "两者语义相近但词性不同：";
            var usageTip = (targetGroup, candGroup) switch
            {
                ("prep", "v") => $"{targetWord} 是介词，后接名词/代词短语；{candWord} 是动词，直接作谓语，句中不可直接互换。",
                ("v", "prep") => $"{targetWord} 是动词，在句中作谓语；{candWord} 是介词，引导介词短语，不可直接互换。",
                ("prep", "conj") => $"{targetWord} 是介词，后接名词或动名词短语；{candWord} 是连词，后接完整从句。",
                ("conj", "prep") => $"{targetWord} 是连词，后接完整从句；{candWord} 是介词，后接名词短语。",
                ("prep", "n") or ("v", "n") => $"{targetWord} 为{tName}，而 {candWord} 为名词（常作主语、宾语或与介词搭配）。",
                _ => $"{targetWord} 为{tName}（{targetCore}），{candWord} 为{cName}（{candCore}），在句子中充当的语法成分不同。"
            };
            return commonPart + usageTip;
        }

        if (targetGroup != candGroup && targetGroup.Length > 0 && candGroup.Length > 0)
        {
            var commonPart = shared.Count > 0 ? $"两者都涉及「{sharedStr}」：" : "";
            return $"{commonPart}{targetWord} 主作{tName}（{targetCore}），而 {candWord} 常用作{cName}（{candCore}），使用时注意词性与主从语义差别。";
        }

        if (shared.Count > 0 && (targetUnique.Length > 0 || candUnique.Length > 0))
        {
            var tFocus = targetUnique.Length > 0 ? string.Join("、", targetUnique.Take(2)) : targetCore;
            var cFocus = candUnique.Length > 0 ? string.Join("、", candUnique.Take(2)) : candCore;
            return $"同为{tName}且都含「{sharedStr}」之意，但侧重点不同：{targetWord} 更强调「{tFocus}」，而 {candWord} 更偏向「{cFocus}」。";
        }

        if (shared.Count > 0)
            return $"同为{tName}，核心含义均指向「{sharedStr}」，语义高度接近，可结合下方固定搭配与常见语境区分使用习惯。";

        return $"{targetWord} 核心侧重「{targetCore}」，而 {candWord} 侧重「{candCore}」，在具体语境和搭配对象上有所区别。";
    }

    private static string BuildSimilarVerdict(
        string targetWord, string targetPos, string targetGroup, string targetCore,
        string candWord, string candPos, string candGroup, string candCore,
        List<string> shared, string spellingDiff, string targetPhonetic, string candPhonetic)
    {
        var sb = new StringBuilder();
        sb.Append(spellingDiff).Append('。');

        if (targetPhonetic.Length > 0 && candPhonetic.Length > 0)
        {
            var sameSound = string.Equals(
                targetPhonetic.Trim('/').Replace("ˈ", "").Replace("ˌ", ""),
                candPhonetic.Trim('/').Replace("ˈ", "").Replace("ˌ", ""),
                StringComparison.Ordinal);
            if (sameSound) sb.Append("读音相同或极相近，听写与阅读时更易混淆。");
        }

        var posDiff = targetGroup.Length > 0 && candGroup.Length > 0 && targetGroup != candGroup;
        if (shared.Count > 0)
        {
            sb.Append($"两者虽易与「{string.Join("、", shared.Take(2))}」一起联想，但 ");
        }
        sb.Append($"{targetWord}（{(targetPos.Length > 0 ? targetPos + " " : "")}{targetCore}）与 {candWord}（{(candPos.Length > 0 ? candPos + " " : "")}{candCore}）");
        if (posDiff)
            sb.Append($"不仅语义不同，主词性也有区分（{PosName(targetGroup)} vs {PosName(candGroup)}），可通过句子成分快速排除。");
        else
            sb.Append("词义不同，建议结合下方差异字母与助记线索对比记忆。");
        return sb.ToString();
    }

    public static (LetterSpan[] TargetSpans, LetterSpan[] CandidateSpans, string Summary) BuildSpellingComparison(string target, string candidate)
    {
        var a = target.ToLowerInvariant();
        var b = candidate.ToLowerInvariant();

        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;

        var midA = target.Substring(prefix, target.Length - prefix - suffix);
        var midB = candidate.Substring(prefix, candidate.Length - prefix - suffix);

        var tSpans = new List<LetterSpan>(3);
        var cSpans = new List<LetterSpan>(3);

        if (prefix > 0)
        {
            tSpans.Add(new LetterSpan(target[..prefix], false));
            cSpans.Add(new LetterSpan(candidate[..prefix], false));
        }
        if (midA.Length > 0) tSpans.Add(new LetterSpan(midA, true));
        if (midB.Length > 0) cSpans.Add(new LetterSpan(midB, true));
        if (suffix > 0)
        {
            tSpans.Add(new LetterSpan(target[(target.Length - suffix)..], false));
            cSpans.Add(new LetterSpan(candidate[(candidate.Length - suffix)..], false));
        }

        // Human-readable diff tip
        var loA = midA.ToLowerInvariant();
        var loB = midB.ToLowerInvariant();
        string summary;
        if (loA.Length == 2 && loB.Length == 2 && loA[0] == loB[1] && loA[1] == loB[0])
        {
            summary = $"第 {prefix + 1}–{prefix + 2} 位相邻字母「{loA} / {loB}」顺序易颠倒";
        }
        else if ((loA.Length == 0 && loB.Length == 1) || (loB.Length == 0 && loA.Length == 1))
        {
            var extraChar = loA.Length == 1 ? loA[0] : loB[0];
            var longer = loA.Length == 1 ? target : candidate;
            var isDouble = (prefix > 0 && a[prefix - 1] == extraChar)
                || (suffix > 0 && a[a.Length - suffix] == extraChar);
            if (isDouble)
                summary = $"字母「{extraChar}」单双写之差（{longer} 为双写 {extraChar}{extraChar}）";
            else if (prefix == 0)
                summary = $"词首多出字母「{extraChar}-」（{longer}）";
            else if (suffix == 0)
                summary = $"词尾多出字母「-{extraChar}」（{longer}）";
            else
                summary = $"第 {prefix + 1} 位多出字母「{extraChar}」（{longer}）";
        }
        else if (prefix == 0 && suffix >= 2)
        {
            summary = $"首部「{(loA.Length > 0 ? loA : "无")} / {(loB.Length > 0 ? loB : "无")}」之差";
        }
        else if (suffix == 0 && prefix >= 2)
        {
            summary = $"词尾「{(loA.Length > 0 ? "-" + loA : "无")} / {(loB.Length > 0 ? "-" + loB : "无")}」之差";
        }
        else if (loA.Length <= 2 && loB.Length <= 2)
        {
            summary = $"第 {prefix + 1} 位「{(loA.Length > 0 ? loA : "无")} / {(loB.Length > 0 ? loB : "无")}」一处之差";
        }
        else
        {
            summary = $"拼写相近，核心差异在高亮片段「{loA} / {loB}」";
        }

        return (tSpans.ToArray(), cSpans.ToArray(), summary);
    }

    private static string? ReadMnemonicSummary(MnemonicStore? mnemonics, string word, string? accountKey)
    {
        if (mnemonics == null) return null;
        var first = mnemonics.Get(word, accountKey).FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(first)) return null;
        var clean = Regex.Replace(first.Trim(), @"\s+", " ");
        return clean.Length <= 96 ? clean : clean[..93] + "…";
    }

    private static string FormatPhonetic(string? phonetic) =>
        string.IsNullOrWhiteSpace(phonetic) ? "" : "/" + phonetic.Trim().Trim('/') + "/";

    private static string CompactMeaning(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var lines = displayMeaning.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join("；", lines.Take(2));
        return joined.Length <= 72 ? joined : joined[..70].TrimEnd('；', '，', '、', ' ') + "…";
    }

    private static string BriefPrimarySense(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var first = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var (_, rest) = DictionaryEntry.SplitDisplaySense(first);
        var cleaned = AnnotationPattern().Replace(rest.Length > 0 ? rest : first, "");
        var pieces = cleaned.Split(['；', ';', '，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        var joined = pieces.Length > 0 ? string.Join("、", pieces) : first;
        return joined.Length <= 26 ? joined : joined[..24].TrimEnd('、', '，', '；', ' ') + "…";
    }

    private static string ExtractPrimaryPos(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var first = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return DictionaryEntry.SplitDisplaySense(first).PartOfSpeech.Trim();
    }

    private static string[] ExtractAllPosLabels(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return [];
        return displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => DictionaryEntry.SplitDisplaySense(line).PartOfSpeech.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> ExtractAllPosGroups(string? displayMeaning)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in ExtractAllPosLabels(displayMeaning))
        {
            var g = NormalizePosBucket(label);
            if (g.Length > 0) set.Add(g);
        }
        return set;
    }

    private static string NormalizePosBucket(string pos)
    {
        var p = pos.ToLowerInvariant();
        if (p.StartsWith("prep")) return "prep";
        if (p.StartsWith("conj")) return "conj";
        if (p.StartsWith("vt") || p.StartsWith("vi") || p.StartsWith("v.")) return "v";
        if (p.StartsWith("adj") || p.StartsWith("a.")) return "adj";
        if (p.StartsWith("adv") || p.StartsWith("ad.")) return "adv";
        if (p.StartsWith("n.")) return "n";
        return "";
    }

    private static string PosName(string group) => group switch
    {
        "v" => "动词",
        "n" => "名词",
        "adj" => "形容词",
        "adv" => "副词",
        "prep" => "介词",
        "conj" => "连词",
        _ => ""
    };

    private static string[] ExtractSenseTokens(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return [];
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (_, rest) = DictionaryEntry.SplitDisplaySense(line);
            var cleaned = AnnotationPattern().Replace(rest.Length > 0 ? rest : line, "");
            foreach (var piece in cleaned.Split(['，', ',', '；', ';', '、', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var token = piece.Trim();
                if (token.Length > 2 && (token.EndsWith('的') || token.EndsWith('地'))) token = token[..^1];
                if (token.Length >= 2 && ChineseCharPattern().IsMatch(token) && seen.Add(token))
                {
                    list.Add(token);
                    if (list.Count >= 8) return list.ToArray();
                }
            }
        }
        return list.ToArray();
    }

    private static List<string> FindSharedSenses(string[] targetSenses, string[] candSenses)
    {
        var shared = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in targetSenses)
        {
            foreach (var c in candSenses)
            {
                if (t == c && seen.Add(t)) { shared.Add(t); continue; }
                if (t.Length >= 2 && c.Length >= 2 && (t.Contains(c, StringComparison.Ordinal) || c.Contains(t, StringComparison.Ordinal)))
                {
                    var picked = t.Length <= c.Length ? t : c;
                    if (seen.Add(picked)) shared.Add(picked);
                }
            }
        }
        return shared;
    }
}
