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
    string? CandidateMnemonic,
    string MemoryHook = "");

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

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|（[^）]*）", RegexOptions.CultureInvariant)]
    private static partial Regex AnnotationPattern();

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
        var targetSenses = ExtractSenseTokens(targetMeaning);
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

        var curatedGroup = normalizedMode == "similar" ? WordConfusionBank.FindGroup(targetWord) : null;
        var targetMemoryHook = normalizedMode == "similar"
            ? BuildMemoryHook(targetWord, targetCore, targetPrimaryPos, targetMnemonic, curatedGroup)
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
                curatedGroup?.SharedAnchor ?? "",
                targetMemoryHook,
                BuildOverviewSpans(targetWord, "", WordConfusionBank.FindHook(targetWord)?.HighlightPart));
        }

        var taken = sourceList.Take(12).ToList();
        var sharedStem = normalizedMode == "similar" ? DetectSharedStem(targetWord, taken.Select(t => t.Word)) : "";
        var groupAnchor = normalizedMode == "similar"
            ? BuildGroupAnchor(targetWord, curatedGroup, sharedStem, mnemonics, accountKey)
            : "";
        var targetOverviewSpans = normalizedMode == "similar"
            ? BuildOverviewSpans(targetWord, sharedStem, WordConfusionBank.FindHook(targetWord)?.HighlightPart)
            : null;

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
            var candSenses = ExtractSenseTokens(candMeaning);

            var shared = FindSharedSenses(targetSenses, candSenses);
            var targetUnique = targetSenses.Where(s => !shared.Contains(s)).Take(3).ToArray();
            var candUnique = candSenses.Where(s => !shared.Contains(s)).Take(3).ToArray();

            var candHookMeta = normalizedMode == "similar" ? WordConfusionBank.FindHook(candWord) : null;
            var (targetSpans, candSpans, spellingDiff) = BuildSpellingComparison(targetWord, candWord, sharedStem, candHookMeta?.HighlightPart);
            var posSummary = BuildPosSummary(targetWord, targetPrimaryPos, targetPosGroup, targetAllPosGroups, candWord, candPrimaryPos, candPosGroup, candAllPosGroups);
            var candPhrases = normalizedMode == "synonym"
                ? relatedWords.LookupPhrases(candWord, cancellationToken).Select(p => p.Word).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray()
                : [];
            var candMnemonic = ReadMnemonicSummary(mnemonics, candWord, accountKey);
            var memoryHook = normalizedMode == "similar"
                ? BuildMemoryHook(candWord, candCore, candPrimaryPos, candMnemonic, curatedGroup)
                : "";

            var verdict = normalizedMode == "similar"
                ? BuildSimilarMemorySummary(targetWord, candWord, targetPhonetic, candPhonetic, targetPrimaryPos, candPrimaryPos, memoryHook)
                : BuildSynonymVerdict(targetWord, targetPrimaryPos, targetPosGroup, targetAllPosGroups, targetCore, targetUnique, candWord, candPrimaryPos, candPosGroup, candAllPosGroups, candCore, candUnique, shared);

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
                candMnemonic,
                memoryHook));
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
            groupAnchor,
            targetMemoryHook,
            targetOverviewSpans);
    }

    private static string BuildGroupAnchor(
        string targetWord,
        ConfusionGroup? curatedGroup,
        string sharedStem,
        MnemonicStore? mnemonics,
        string? accountKey)
    {
        if (curatedGroup != null && !string.IsNullOrWhiteSpace(curatedGroup.SharedAnchor))
            return curatedGroup.SharedAnchor;

        // Try pulling a leading root clue from the target word's local mnemonic breakdown
        if (mnemonics != null)
        {
            var note = mnemonics.Get(targetWord, accountKey).FirstOrDefault();
            if (note != null)
            {
                var parts = MnemonicParts.Parse(targetWord, note.NoteType, note.Text);
                var firstPart = parts.FirstOrDefault()?.Parts.FirstOrDefault(p => p.Meaning.Length > 0);
                if (firstPart != null && sharedStem.Length >= 2)
                {
                    var normForm = MnemonicParts.NormalizeFormKey(firstPart.Form).Trim('-');
                    if (normForm.Length >= 2 && (normForm.StartsWith(sharedStem, StringComparison.OrdinalIgnoreCase)
                        || sharedStem.StartsWith(normForm, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"共同线索 {firstPart.Form}（{firstPart.Meaning}）· 对比高亮差异部分来串记";
                    }
                }
            }
        }

        if (sharedStem.Length >= 3)
            return $"共同字形片段「{sharedStem}-」· 抓高亮差异部分，结合拆解助记一口气分清";
        return "抓每个词的高亮差异字母与拆解线索，对比串记更不容易混";
    }

    private static string BuildMemoryHook(
        string word,
        string coreSense,
        string primaryPos,
        string? localMnemonic,
        ConfusionGroup? group)
    {
        // 1. Curated memory-first hook (highest quality: associative / root-suffix / visual letter cue)
        var curated = group?.Members.FirstOrDefault(m => m.Word.Equals(word, StringComparison.OrdinalIgnoreCase))
            ?? WordConfusionBank.FindHook(word);
        if (curated != null && !string.IsNullOrWhiteSpace(curated.Hook))
            return curated.Hook;

        // 2. Local 10,614-word mnemonic formula (already rich with root/affix + associative explanations!)
        if (!string.IsNullOrWhiteSpace(localMnemonic))
            return localMnemonic;

        // 3. Structural prefix/suffix cue for words without a local mnemonic
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

    private static string BuildSimilarMemorySummary(
        string targetWord,
        string candWord,
        string targetPhonetic,
        string candPhonetic,
        string targetPos,
        string candPos,
        string memoryHook)
    {
        if (!string.IsNullOrWhiteSpace(memoryHook))
            return memoryHook;

        if (targetPhonetic.Length > 0 && candPhonetic.Length > 0)
        {
            var sameSound = string.Equals(
                targetPhonetic.Trim('/').Replace("ˈ", "").Replace("ˌ", ""),
                candPhonetic.Trim('/').Replace("ˈ", "").Replace("ˌ", ""),
                StringComparison.Ordinal);
            if (sameSound)
                return $"与 {targetWord} 读音极近，建议结合词性（{(candPos.Length > 0 ? candPos : "注意词性")}）与拼写亮点区分记忆。";
        }
        return "";
    }

    private static string DetectSharedStem(string targetWord, IEnumerable<string> candidates)
    {
        var a = targetWord.ToLowerInvariant();
        var list = candidates.Select(c => c.ToLowerInvariant()).Take(6).ToArray();
        if (list.Length == 0) return "";

        var best = "";
        for (var len = Math.Min(a.Length - 1, 9); len >= 2; len--)
        {
            var prefix = a[..len];
            var matchCount = list.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
            if (matchCount >= Math.Max(2, (list.Length + 1) / 2))
            {
                best = prefix;
                break;
            }
        }
        return best;
    }

    private static LetterSpan[] BuildOverviewSpans(string word, string sharedStem, string? curatedHighlight)
    {
        if (!string.IsNullOrWhiteSpace(curatedHighlight))
        {
            var startSearch = sharedStem.Length >= 2
                && word.StartsWith(sharedStem, StringComparison.OrdinalIgnoreCase)
                && sharedStem.Length < word.Length
                    ? sharedStem.Length
                    : 0;
            var idx = word.IndexOf(curatedHighlight, startSearch, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 && startSearch > 0)
                idx = word.IndexOf(curatedHighlight, 0, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && curatedHighlight.Length < word.Length)
            {
                var spans = new List<LetterSpan>(3);
                if (idx > 0) spans.Add(new LetterSpan(word[..idx], false));
                spans.Add(new LetterSpan(word.Substring(idx, curatedHighlight.Length), true));
                var end = idx + curatedHighlight.Length;
                if (end < word.Length) spans.Add(new LetterSpan(word[end..], false));
                return spans.ToArray();
            }
        }

        if (sharedStem.Length >= 2 && word.StartsWith(sharedStem, StringComparison.OrdinalIgnoreCase) && word.Length > sharedStem.Length)
        {
            return
            [
                new LetterSpan(word[..sharedStem.Length], false),
                new LetterSpan(word[sharedStem.Length..], true)
            ];
        }

        return [new LetterSpan(word, false)];
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

    public static (LetterSpan[] TargetSpans, LetterSpan[] CandidateSpans, string Summary) BuildSpellingComparison(
        string target, string candidate, string sharedStem = "", string? curatedHighlight = null)
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

        // If a curated highlight part exists for the candidate (e.g. statu[re], des[s]ert, compl[e]ment),
        // prefer that pinpoint highlight so the visual cue directly matches the memory hook!
        if (!string.IsNullOrWhiteSpace(curatedHighlight))
        {
            var curatedSpans = BuildOverviewSpans(candidate, sharedStem, curatedHighlight);
            if (curatedSpans.Any(s => s.Highlight))
                cSpans = curatedSpans.ToList();
        }

        return (tSpans.ToArray(), cSpans.ToArray(), "");
    }

    private static string? ReadMnemonicSummary(MnemonicStore? mnemonics, string word, string? accountKey)
    {
        if (mnemonics == null) return null;
        var first = mnemonics.Get(word, accountKey).FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(first)) return null;
        // Take the first informative line so multi-line notes stay clean and punchy
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
