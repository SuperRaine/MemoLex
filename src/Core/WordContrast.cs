using System.Globalization;
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
    string? CandidateMnemonic,
    string MemoryHook = "",
    string TargetUsageNote = "",
    string CandidateUsageNote = "");

public sealed record WordContrastReport(
    string Mode,
    string TargetWord,
    string TargetPhonetic,
    string TargetPrimaryPos,
    string TargetMeaning,
    string TargetCoreSense,
    string? TargetMnemonic,
    string[] TargetPhrases,
    IReadOnlyList<WordContrastEntry> Items,
    string GroupAnchor = "",
    string TargetMemoryHook = "",
    LetterSpan[]? TargetOverviewSpans = null);

public static partial class WordContrast
{
    [GeneratedRegex(@"[\u4E00-\u9FFF]", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseCharPattern();

    [GeneratedRegex(@"\[[^\]]*\]|［[^］]*］|【[^】]*】|\([^)]*\)|（[^）]*）", RegexOptions.CultureInvariant)]
    private static partial Regex AnnotationPattern();

    [GeneratedRegex(@"^(?:［[^］]{1,6}］|\[[^\]]{1,6}\]|【[^】]{1,6}】)", RegexOptions.CultureInvariant)]
    private static partial Regex DomainTagPrefixPattern();

    private static readonly (string Prefix, string Meaning)[] CommonPrefixes =
    [
        ("counter", "相反、对抗"), ("inter", "在…之间、相互"), ("super", "超、在上面"),
        ("trans", "跨越、转移"), ("under", "在下面、不足"), ("over", "过度、在上面"),
        ("intro", "向内、进入"), ("micro", "微小"), ("multi", "多"),
        ("anti", "反对、抗"), ("auto", "自动、自己"), ("fore", "在前、预先"),
        ("post", "在后"), ("semi", "半"), ("tele", "远距离"),
        ("com", "共同、一起"), ("con", "共同、一起"), ("dis", "分开、否定"),
        ("mis", "错误"), ("non", "非、不"), ("out", "向外、超过"),
        ("per", "贯穿、彻底"), ("pre", "预先、在前"), ("pro", "向前、支持"),
        ("sub", "在下面、次级"), ("sur", "超过、在上面"), ("sym", "共同、相同"),
        ("syn", "共同、相同"), ("uni", "单一"),
        ("ab", "离开、偏离"), ("ad", "朝向、加强"), ("de", "向下、去除"),
        ("en", "使成为"), ("ex", "向外、出"), ("im", "向内/否定"),
        ("in", "向内/否定"), ("ob", "迎面、反对"), ("re", "再次、回"),
        ("un", "否定、打开")
    ];

    private static readonly (string Suffix, string Meaning)[] CommonSuffixes =
    [
        ("ation", "动作或结果（名词）"), ("ition", "状态或过程（名词）"), ("ology", "…学、研究"),
        ("ship", "身份、关系或状态"), ("ment", "行为或产物（名词）"), ("ness", "抽象性质（名词）"),
        ("tion", "动作、状态或场所"), ("sion", "动作或状态"), ("ture", "状态、结果或形象"),
        ("ance", "性质或状态"), ("ence", "性质或状态"), ("ical", "与…相关的（形容词）"),
        ("able", "能够/值得…的"), ("ible", "能够…的"), ("less", "无、缺乏…的"),
        ("tive", "具有…倾向的"), ("eous", "充满…性质的"), ("ious", "充满…的"),
        ("ize", "使…化（动词）"), ("ise", "使…化（动词）"), ("ify", "使成为…（动词）"),
        ("ful", "充满…的"), ("ous", "充满…的"), ("ive", "具有…性质的"),
        ("ary", "相关属性/场所"), ("ery", "场所/集合/性质"), ("ory", "场所/属性"),
        ("ist", "从事…的人"), ("ism", "主义、现象"), ("ity", "性质、状态"),
        ("tic", "…的、属于…的"), ("ic", "…的、相关的"), ("al", "…的（形容词）"),
        ("ed", "感到/已…的"), ("er", "做…的人或物"), ("or", "做…的人或物"),
        ("ly", "…地（副词）"), ("y", "多…的、有…特征的")
    ];

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
        var targetWord = targetEntry?.word ?? cleanWord;
        var targetMeaning = targetEntry?.DisplayMeaning ?? "";
        var targetPhonetic = FormatPhonetic(targetEntry?.phonetic);
        var targetPrimaryPos = ExtractPrimaryPos(targetMeaning);
        var targetPosGroup = NormalizePosBucket(targetPrimaryPos);
        var targetAllPosGroups = ExtractAllPosGroups(targetMeaning);
        var targetCore = BriefPrimarySense(targetMeaning);
        var targetSenses = ExtractSenseTokens(targetMeaning, posGroupFilter: null);
        var targetMnemonic = ReadMnemonicSummary(mnemonics, targetWord, accountKey);
        var targetPhrases = relatedWords.LookupPhrases(targetWord, cancellationToken)
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
            else if (idx < 0 && !focusWord.Trim().Equals(targetWord, StringComparison.OrdinalIgnoreCase))
            {
                var focusEntry = dictionary.Lookup(focusWord.Trim());
                if (focusEntry != null && !string.IsNullOrWhiteSpace(focusEntry.DisplayMeaning))
                    sourceList.Insert(0, new RelatedWord(focusEntry.word, focusEntry.DisplayMeaning));
            }
        }

        var targetCuratedGroup = normalizedMode == "similar" ? WordConfusionBank.FindGroup(targetWord) : null;
        var targetMemoryHook = normalizedMode == "similar"
            ? BuildMemoryHook(targetWord, targetCore, targetPrimaryPos, targetMnemonic, targetCuratedGroup)
            : "";

        if (sourceList.Count == 0)
        {
            return new WordContrastReport(
                normalizedMode,
                targetWord,
                targetPhonetic,
                targetPrimaryPos,
                CompactMeaning(targetMeaning),
                targetCore,
                targetMnemonic,
                targetPhrases,
                [],
                "",
                targetMemoryHook,
                null);
        }

        var taken = sourceList.Take(12).ToList();
        var targetRegister = DescribeRegister(targetEntry, dictionary.FrequencyRank(targetWord));
        var items = new List<WordContrastEntry>(taken.Count);

        foreach (var rel in taken)
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

            var sharedPosGroup = targetPosGroup.Length > 0 && candAllPosGroups.Contains(targetPosGroup)
                ? targetPosGroup
                : (candPosGroup.Length > 0 && targetAllPosGroups.Contains(candPosGroup) ? candPosGroup : null);

            var candSenses = ExtractSenseTokens(candMeaning, posGroupFilter: null);
            var shared = FindSharedSenses(targetSenses, candSenses);

            // For non-curated usage contrast, prefer clean senses within the shared POS so unrelated
            // cross-POS meanings (e.g. noun glosses when comparing verbs) don't pollute the comparison.
            var tScopedSenses = sharedPosGroup != null ? ExtractSenseTokens(targetMeaning, sharedPosGroup) : targetSenses;
            var cScopedSenses = sharedPosGroup != null ? ExtractSenseTokens(candMeaning, sharedPosGroup) : candSenses;
            if (tScopedSenses.Length == 0) tScopedSenses = targetSenses;
            if (cScopedSenses.Length == 0) cScopedSenses = candSenses;

            var targetUnique = tScopedSenses.Where(s => !IsCoveredByShared(s, shared)).Take(3).ToArray();
            var candUnique = cScopedSenses.Where(s => !IsCoveredByShared(s, shared)).Take(3).ToArray();

            var posSummary = BuildPosSummary(targetWord, targetPrimaryPos, targetPosGroup, targetAllPosGroups, candWord, candPrimaryPos, candPosGroup, candAllPosGroups);
            var candPhrases = normalizedMode == "synonym"
                ? relatedWords.LookupPhrases(candWord, cancellationToken).Select(p => p.Word).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray()
                : [];
            var candMnemonic = ReadMnemonicSummary(mnemonics, candWord, accountKey);

            // Pairwise spelling diff: strictly between targetWord and THIS specific candWord!
            var sharedConfGroup = normalizedMode == "similar" ? FindSharedConfusionGroup(targetWord, candWord) : null;
            var (targetSpans, candSpans, spellingDiff) = BuildPairwiseSpellingComparison(targetWord, candWord, sharedConfGroup);

            var memoryHook = normalizedMode == "similar"
                ? BuildMemoryHook(candWord, candCore, candPrimaryPos, candMnemonic, sharedConfGroup ?? WordConfusionBank.FindGroup(candWord))
                : "";

            string verdict;
            string targetUsageNote = "";
            string candUsageNote = "";
            string[] sharedDisplay = shared.Take(3).ToArray();

            if (normalizedMode == "similar")
            {
                verdict = "";
            }
            else
            {
                var curatedPair = WordSynonymBank.FindPair(targetWord, candWord);
                if (curatedPair is { } pair)
                {
                    sharedDisplay = [pair.Group.SharedMeaning];
                    targetUsageNote = pair.Target.UsageDirection;
                    candUsageNote = pair.Candidate.UsageDirection;
                    verdict = $"两者都含「{pair.Group.SharedMeaning}」之意，区别在于{pair.Group.DifferenceAxis}：{targetWord} {pair.Target.UsageDirection}；而 {candWord} {pair.Candidate.UsageDirection}。";
                }
                else
                {
                    var candRegister = DescribeRegister(entry, dictionary.FrequencyRank(candWord));
                    (verdict, targetUsageNote, candUsageNote) = BuildSynonymUsageVerdict(
                        targetWord, targetPrimaryPos, targetPosGroup, targetAllPosGroups, targetCore, targetUnique, targetRegister,
                        candWord, candPrimaryPos, candPosGroup, candAllPosGroups, candCore, candUnique, candRegister,
                        shared);
                }
            }

            items.Add(new WordContrastEntry(
                candWord,
                candPhonetic,
                candPrimaryPos,
                candAllPos,
                CompactMeaning(candMeaning),
                candCore,
                sharedDisplay,
                targetUnique,
                candUnique,
                posSummary,
                verdict,
                spellingDiff,
                targetSpans,
                candSpans,
                targetPhrases,
                candPhrases,
                candMnemonic,
                memoryHook,
                targetUsageNote,
                candUsageNote));
        }

        return new WordContrastReport(
            normalizedMode,
            targetWord,
            targetPhonetic,
            targetPrimaryPos,
            CompactMeaning(targetMeaning),
            targetCore,
            targetMnemonic,
            targetPhrases,
            items,
            "",
            targetMemoryHook,
            null);
    }

    private static ConfusionGroup? FindSharedConfusionGroup(string a, string b)
    {
        var group = WordConfusionBank.FindGroup(a);
        if (group == null) return null;
        return group.Members.Any(m => m.Word.Equals(b, StringComparison.OrdinalIgnoreCase)) ? group : null;
    }

    private static string BuildMemoryHook(
        string word,
        string coreSense,
        string primaryPos,
        string? localMnemonic,
        ConfusionGroup? group)
    {
        var curated = group?.Members.FirstOrDefault(m => m.Word.Equals(word, StringComparison.OrdinalIgnoreCase))
            ?? WordConfusionBank.FindHook(word);
        if (curated != null && !string.IsNullOrWhiteSpace(curated.Hook))
            return curated.Hook;

        if (!string.IsNullOrWhiteSpace(localMnemonic))
            return localMnemonic;

        var low = word.ToLowerInvariant();
        foreach (var (prefix, pMeaning) in CommonPrefixes)
        {
            if (low.Length >= prefix.Length + 3 && low.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = low[prefix.Length..];
                return $"{prefix}-（{pMeaning}）+ {rest} → {coreSense}";
            }
        }
        foreach (var (suffix, sMeaning) in CommonSuffixes)
        {
            if (low.Length >= suffix.Length + 3 && low.EndsWith(suffix, StringComparison.Ordinal))
            {
                var stem = low[..^suffix.Length];
                return $"{stem} + -{suffix}（{sMeaning}）→ {coreSense}";
            }
        }

        var posTag = primaryPos.Length > 0 ? primaryPos + " " : "";
        return string.IsNullOrWhiteSpace(coreSense) ? "" : $"{posTag}{coreSense}";
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
            return $"主词性不同：{targetWord} 主作{tName}，{candWord} 主作{cName}";
        return $"词性不同：{tName}（{targetPos}） vs {cName}（{candPos}）";
    }

    private readonly record struct RegisterProfile(string Label, int Level);

    private static RegisterProfile DescribeRegister(DictionaryEntry? entry, int frequencyRank)
    {
        if (entry == null) return new("常用表达", 2);
        var tags = (entry.tag ?? "").ToLowerInvariant();
        if (tags.Contains("zk") || tags.Contains("gk") || frequencyRank <= 2500)
            return new("日常高频通用词", 1);
        if (tags.Contains("cet4") || entry.oxford == "1" || frequencyRank <= 7500)
            return new("常用中性表达", 2);
        if (tags.Contains("gre") || tags.Contains("toefl") || frequencyRank > 16000)
            return new("书面/正式高阶词", 3);
        return new("常见书面表达", 2);
    }

    private static (string Verdict, string TargetUsage, string CandUsage) BuildSynonymUsageVerdict(
        string targetWord, string targetPos, string targetGroup, HashSet<string> targetAll, string targetCore, string[] targetUnique, RegisterProfile targetReg,
        string candWord, string candPos, string candGroup, HashSet<string> candAll, string candCore, string[] candUnique, RegisterProfile candReg,
        List<string> shared)
    {
        var tName = PosName(targetGroup);
        var cName = PosName(candGroup);
        var sharedStr = string.Join("、", shared.Take(2));
        var strictPosDiff = targetGroup.Length > 0 && candGroup.Length > 0 && !candAll.Contains(targetGroup) && !targetAll.Contains(candGroup);

        if (strictPosDiff)
        {
            var commonPart = shared.Count > 0 ? $"两者都含「{sharedStr}」之意，但词性与句法用法不同：" : "两者语义相近但词性与句法位置不同：";
            var (tUse, cUse, tip) = (targetGroup, candGroup) switch
            {
                ("prep", "v") => ("介词，后接名词或动名词短语，不能单独作谓语", "动词，在句中直接作谓语接宾语", $"{targetWord} 是介词，后接名词/代词短语；{candWord} 是动词，直接作谓语，句中不可直接互换。"),
                ("v", "prep") => ("动词，在句中直接作谓语", "介词，引导介词短语，不能作谓语", $"{targetWord} 是动词，在句中作谓语；{candWord} 是介词，引导介词短语，不可直接互换。"),
                ("prep", "conj") => ("介词，后接名词或动名词短语", "连词，后接完整主谓从句", $"{targetWord} 是介词，后接名词短语；{candWord} 是连词，后接完整从句。"),
                ("conj", "prep") => ("连词，后接完整主谓从句", "介词，后接名词或动名词短语", $"{targetWord} 是连词，后接完整从句；{candWord} 是介词，后接名词短语。"),
                ("v", "n") => ($"动词，直接表达「{targetCore}」动作", $"名词，常作主语、宾语或与介词搭配", $"{targetWord} 为动词，表达具体动作；{candWord} 为名词，常作主语、宾语或介词宾语。"),
                ("n", "v") => ($"名词，常作主语或宾语指称「{targetCore}」", $"动词，在句中直接作谓语动作", $"{targetWord} 为名词，用于指称概念；{candWord} 为动词，在句中直接作谓语。"),
                _ => ($"{tName}（{targetPos}），用于表达「{targetCore}」", $"{cName}（{candPos}），用于表达「{candCore}」", $"{targetWord} 为{tName}，{candWord} 为{cName}，在句中充当的语法成分不同。")
            };
            return (commonPart + tip, tUse, cUse);
        }

        // Same verb category: check transitivity (vt. vs vi.)
        var tIsVt = targetPos.StartsWith("vt", StringComparison.OrdinalIgnoreCase);
        var tIsVi = targetPos.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
        var cIsVt = candPos.StartsWith("vt", StringComparison.OrdinalIgnoreCase);
        var cIsVi = candPos.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
        if (targetGroup == "v" && candGroup == "v" && ((tIsVt && cIsVi) || (tIsVi && cIsVt)))
        {
            var tUse = tIsVt ? $"及物动词（vt.），可直接接宾语；{targetReg.Label}" : $"不及物动词（vi.），接对象时通常需配介词；{targetReg.Label}";
            var cUse = cIsVt ? $"及物动词（vt.），可直接接宾语；{candReg.Label}" : $"不及物动词（vi.），接对象时通常需配介词；{candReg.Label}";
            var head = shared.Count > 0 ? $"两者同为动词且都含「{sharedStr}」之意，但及物性与用法不同：" : "两者同为动词，但及物性与句式搭配不同：";
            var verdict = tIsVt
                ? $"{head}{targetWord} 主作及物动词可直接接宾语；{candWord} 主作不及物动词，接对象时通常需搭配介词。"
                : $"{head}{targetWord} 主作不及物动词，接对象时常需配介词；{candWord} 主作及物动词，可直接接宾语。";
            return (verdict, tUse, cUse);
        }

        if (targetGroup != candGroup && targetGroup.Length > 0 && candGroup.Length > 0)
        {
            var commonPart = shared.Count > 0 ? $"两者都涉及「{sharedStr}」，但主要词性与用法方向不同：" : "两者词性侧重不同：";
            var tUse = $"主作{tName}（{targetPos}），{targetReg.Label}" + (targetUnique.Length > 0 ? $"；也延伸至「{string.Join("、", targetUnique.Take(2))}」" : "");
            var cUse = $"主作{cName}（{candPos}），{candReg.Label}" + (candUnique.Length > 0 ? $"；也延伸至「{string.Join("、", candUnique.Take(2))}」" : "");
            return ($"{commonPart}{targetWord} 主作{tName}（{targetCore}），而 {candWord} 更常作{cName}（{candCore}），使用时需注意句法角色与主从语义。", tUse, cUse);
        }

        // Same primary POS: compare register + semantic direction
        var registerDiff = targetReg.Level != candReg.Level;
        if (shared.Count > 0)
        {
            var tExtend = targetUnique.Length > 0 ? $"还可延伸表达「{string.Join("、", targetUnique.Take(2))}」" : "";
            var cExtend = candUnique.Length > 0 ? $"还可延伸表达「{string.Join("、", candUnique.Take(2))}」" : "";
            var tUse = targetReg.Label + (tExtend.Length > 0 ? $"，{tExtend}" : $"，侧重「{sharedStr}」");
            var cUse = candReg.Label + (cExtend.Length > 0 ? $"，{cExtend}" : $"，侧重「{sharedStr}」");

            if (registerDiff)
            {
                var verdict = $"两者同为{tName}且都含「{sharedStr}」之意，区别主要在语域与延伸方向：{targetWord} 偏{targetReg.Label}" +
                    (tExtend.Length > 0 ? $"（{tExtend}）" : "") +
                    $"；{candWord} 偏{candReg.Label}" +
                    (cExtend.Length > 0 ? $"（{cExtend}）" : "") + "。";
                return (verdict, tUse, cUse);
            }
            if (targetUnique.Length > 0 || candUnique.Length > 0)
            {
                var tDir = targetUnique.Length > 0 ? $"兼有「{string.Join("、", targetUnique.Take(2))}」方向" : $"语义更聚焦于「{sharedStr}」";
                var cDir = candUnique.Length > 0 ? $"兼有「{string.Join("、", candUnique.Take(2))}」方向" : $"语义更聚焦于「{sharedStr}」";
                return ($"两者同为{tName}，核心均表达「{sharedStr}」，但语义延伸方向不同：{targetWord} {tDir}；{candWord} {cDir}，可结合下方固定搭配选用。", tUse, cUse);
            }
            return ($"两者同为{tName}，核心含义均指向「{sharedStr}」，语域相近，可结合下方常见搭配与上下文语境区分习惯用法。", tUse, cUse);
        }

        var fallbackT = $"{targetReg.Label}，核心义为「{targetCore}」";
        var fallbackC = $"{candReg.Label}，核心义为「{candCore}」";
        return ($"{targetWord}（{targetCore}）与 {candWord}（{candCore}）在具体语境、修辞引申或搭配方向上有所不同，注意避免直接互换。", fallbackT, fallbackC);
    }

    /// <summary>
    /// Computes an exact pairwise visual diff strictly between target and candidate.
    /// Curated pinpoint highlights are applied only when both words belong to the same curated family.
    /// </summary>
    public static (LetterSpan[] TargetSpans, LetterSpan[] CandidateSpans, string Summary) BuildPairwiseSpellingComparison(
        string target, string candidate, ConfusionGroup? sharedGroup = null)
    {
        if (sharedGroup != null)
        {
            var tHook = sharedGroup.Members.FirstOrDefault(m => m.Word.Equals(target, StringComparison.OrdinalIgnoreCase));
            var cHook = sharedGroup.Members.FirstOrDefault(m => m.Word.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (tHook?.HighlightPart is { Length: > 0 } tPart
                && cHook?.HighlightPart is { Length: > 0 } cPart
                && !tPart.Equals(cPart, StringComparison.OrdinalIgnoreCase))
            {
                var commonPrefix = CommonPrefixLen(target, candidate);
                var tSpansCurated = BuildCuratedSpan(target, tPart, commonPrefix);
                var cSpansCurated = BuildCuratedSpan(candidate, cPart, commonPrefix);
                if (tSpansCurated != null && cSpansCurated != null && UnhighlightedContextMatches(tSpansCurated, cSpansCurated))
                    return (tSpansCurated, cSpansCurated, "");
            }
        }

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

        return (tSpans.ToArray(), cSpans.ToArray(), "");
    }

    private static bool UnhighlightedContextMatches(LetterSpan[] a, LetterSpan[] b)
    {
        static string ContextOf(LetterSpan[] spans) =>
            string.Concat(spans.Select(s => s.Highlight ? "|" : s.Text.ToLowerInvariant()));
        return string.Equals(ContextOf(a), ContextOf(b), StringComparison.Ordinal);
    }

    private static int CommonPrefixLen(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
        return i;
    }

    private static LetterSpan[]? BuildCuratedSpan(string word, string part, int commonPrefix)
    {
        if (part.Length >= word.Length) return null;
        var startSearch = commonPrefix >= 2 && commonPrefix < word.Length ? commonPrefix : 0;
        var idx = word.IndexOf(part, startSearch, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && startSearch > 0)
            idx = word.IndexOf(part, 0, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var spans = new List<LetterSpan>(3);
        if (idx > 0) spans.Add(new LetterSpan(word[..idx], false));
        spans.Add(new LetterSpan(word.Substring(idx, part.Length), true));
        var end = idx + part.Length;
        if (end < word.Length) spans.Add(new LetterSpan(word[end..], false));
        return spans.ToArray();
    }

    private static string? ReadMnemonicSummary(MnemonicStore? mnemonics, string word, string? accountKey)
    {
        if (mnemonics == null) return null;
        var first = mnemonics.Get(word, accountKey).FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(first)) return null;
        var lines = first.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = lines.Length > 0 ? lines[0] : first.Trim();
        var clean = Regex.Replace(joined, @"\s+", " ");
        return clean.Length <= 110 ? clean : clean[..107] + "…";
    }

    private static string FormatPhonetic(string? phonetic) =>
        string.IsNullOrWhiteSpace(phonetic) ? "" : "/" + phonetic.Trim().Trim('/') + "/";

    private static string CompactMeaning(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var lines = displayMeaning.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join("；", lines.Take(2));
        return joined.Length <= 76 ? joined : joined[..74].TrimEnd('；', '，', '、', ' ') + "…";
    }

    private static string BriefPrimarySense(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var first = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var (_, rest) = DictionaryEntry.SplitDisplaySense(first);
        var body = rest.Length > 0 ? rest : first;
        var pieces = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawSeg in body.Split(['；', ';', '，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (DomainTagPrefixPattern().IsMatch(rawSeg)) continue;
            var cleaned = AnnotationPattern().Replace(rawSeg, "").Trim();
            if (cleaned.Length > 0 && seen.Add(cleaned))
            {
                pieces.Add(cleaned);
                if (pieces.Count >= 3) break;
            }
        }
        var joined = pieces.Count > 0 ? string.Join("、", pieces) : AnnotationPattern().Replace(body, "").Trim();
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

    private static string[] ExtractSenseTokens(string? displayMeaning, string? posGroupFilter)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return [];
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (pos, rest) = DictionaryEntry.SplitDisplaySense(line);
            if (!string.IsNullOrEmpty(posGroupFilter))
            {
                var lineGroup = NormalizePosBucket(pos);
                if (lineGroup.Length > 0 && lineGroup != posGroupFilter) continue;
            }
            var body = rest.Length > 0 ? rest : line;
            foreach (var rawPiece in body.Split(['，', ',', '；', ';', '、', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // Skip domain-specific / archaic jargon entries like "［生］使外展" or "［古］..."
                if (DomainTagPrefixPattern().IsMatch(rawPiece)) continue;
                var token = AnnotationPattern().Replace(rawPiece, "").Trim();
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

    private static bool IsCoveredByShared(string token, List<string> shared) =>
        shared.Any(s => s == token || (s.Length >= 2 && (token.Contains(s, StringComparison.Ordinal) || s.Contains(token, StringComparison.Ordinal))));

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
