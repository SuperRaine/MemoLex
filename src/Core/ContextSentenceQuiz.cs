using System.Text;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record ContextQuizOption(
    string Label,
    string DisplayText,
    string BaseWord,
    string Phonetic,
    string Meaning,
    string DistractorKind,
    string Analysis,
    bool IsCorrect);

public sealed record ContextQuizQuestion(
    string ItemId,
    string Word,
    string Source,
    string BlankedSentence,
    string FullSentence,
    string Translation,
    string AnswerDisplay,
    string AnswerBaseWord,
    string AnswerPhonetic,
    string AnswerMeaning,
    string? MnemonicHint,
    string ExplanationSummary,
    IReadOnlyList<ContextQuizOption> Options);

public static partial class ContextSentenceQuiz
{
    private static readonly string[] OptionLabels = ["A", "B", "C", "D"];

    private static readonly Dictionary<string, string[]> FallbackPools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["v"] = ["achieve", "acquire", "adapt", "adopt", "affect", "alter", "assess", "assume", "confirm", "convey", "derive", "emerge", "enhance", "establish", "evaluate", "identify", "maintain", "obtain", "perceive", "promote", "reflect", "require", "resolve", "sustain"],
        ["n"] = ["approach", "aspect", "benefit", "concept", "context", "contrast", "decade", "element", "evidence", "factor", "feature", "function", "impact", "insight", "instance", "method", "outcome", "pattern", "process", "purpose", "strategy", "structure", "tendency", "version"],
        ["adj"] = ["accurate", "adequate", "apparent", "complex", "crucial", "distinct", "diverse", "efficient", "essential", "evident", "flexible", "initial", "intense", "obvious", "precise", "primary", "relevant", "reliable", "significant", "specific", "stable", "subtle", "typical", "virtual"],
        ["adv"] = ["actually", "apparently", "approximately", "barely", "constantly", "directly", "effectively", "eventually", "frequently", "gradually", "hardly", "largely", "merely", "mainly", "nearly", "obviously", "precisely", "primarily", "rapidly", "rarely", "roughly", "slightly", "steadily", "virtually"],
        ["prep"] = ["against", "despite", "beyond", "within", "toward", "through", "without", "among", "between", "beside", "beneath", "across", "during", "except", "regarding", "unlike"]
    };

    private static readonly HashSet<string> ModalOrInfinitiveCue = new(StringComparer.OrdinalIgnoreCase)
    {
        "to", "must", "should", "would", "could", "will", "shall", "may", "might", "can", "cannot", "do", "does", "did", "don't", "doesn't", "didn't"
    };

    private static readonly HashSet<string> DeterminerCue = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "these", "those", "my", "your", "his", "her", "its", "our", "their", "any", "some", "every", "each", "no", "such"
    };

    private static readonly HashSet<string> BeOrAuxCue = new(StringComparer.OrdinalIgnoreCase)
    {
        "is", "are", "was", "were", "be", "been", "being", "am", "has", "have", "had"
    };

    private static readonly (string Suffix, string PosHint, string MeaningHint)[] KnownSuffixes =
    [
        ("ation", "名词", "表行为、过程或结果"),
        ("ition", "名词", "表状态或结果"),
        ("tion", "名词", "表行为或抽象概念"),
        ("sion", "名词", "表动作或状态"),
        ("ment", "名词", "表行为或产物"),
        ("ance", "名词", "表性质或状态"),
        ("ence", "名词", "表性质或状态"),
        ("ness", "名词", "表抽象性质"),
        ("ity", "名词", "表性质或程度"),
        ("ship", "名词", "表关系或身份"),
        ("ical", "形容词", "表「具有某种特征/属性」"),
        ("able", "形容词", "表「能够……的/可……的」"),
        ("ible", "形容词", "表「能够……的」"),
        ("tive", "形容词", "表「具有……倾向的」"),
        ("ive", "形容词", "表「有……性质的」"),
        ("ous", "形容词", "表「充满……的」"),
        ("ful", "形容词", "表「富有……的」"),
        ("less", "形容词", "表「缺乏/无……的」"),
        ("al", "形容词", "表「与……相关的」"),
        ("ic", "形容词", "表「……方面的」"),
        ("ly", "副词", "常修饰动词或形容词"),
        ("ize", "动词", "表「使……化」"),
        ("ise", "动词", "表「使……化」"),
        ("ify", "动词", "表「使成为……」"),
        ("er", "名词", "常指「施动者/人或物」"),
        ("or", "名词", "常指「施动者/人或物」"),
        ("ist", "名词", "常指「从事者/主义者」")
    ];

    private static readonly string[][] InterchangeableChineseGroups =
    [
        ["维持", "保持", "维护", "保留", "持续", "继续"],
        ["影响", "作用", "波及", "冲击"],
        ["支持", "支撑", "支援", "拥护", "赞成", "扶助", "帮助", "协助", "援助"],
        ["适应", "使适应", "顺应", "调整", "调节"],
        ["获得", "得到", "取得", "获取", "赢得"],
        ["需要", "要求", "需求", "必要"],
        ["改变", "变化", "更改", "修改", "转变", "变更"],
        ["重要", "重大", "要紧", "关键", "核心", "主要"],
        ["快速", "迅速", "飞快", "快捷", "敏捷"],
        ["困难", "艰难", "艰巨", "棘手", "不易"],
        ["增加", "增长", "增大", "加大", "提高", "提升", "增强", "扩大", "扩张"],
        ["改善", "改进", "改良", "增进", "完善", "变好", "更好"],
        ["减少", "降低", "削减", "缩小", "减轻", "下降"],
        ["建立", "设立", "创建", "创立", "成立", "构建"],
        ["完成", "实现", "达成", "达到", "做到"],
        ["认为", "觉得", "以为", "相信", "主张"],
        ["发现", "找到", "发觉", "察觉", "揭露"],
        ["显示", "表明", "展示", "表现", "呈现", "反映"],
        ["提供", "供给", "供应", "给予", "赋予"],
        ["允许", "准许", "许可", "容许", "批准", "同意"],
        ["保护", "守护", "保全", "防卫", "防御"],
        ["开始", "启动", "开启", "着手", "发起"],
        ["停止", "中止", "终止", "结束", "暂停"],
        ["使用", "利用", "运用", "采用", "应用"],
        ["理解", "明白", "懂得", "领会", "了解"],
        ["决定", "决心", "裁定", "确定", "判定"],
        ["包含", "包括", "含有", "容纳", "涵盖"],
        ["产生", "造成", "引起", "导致", "引发"],
        ["尊重", "尊敬", "敬重", "推崇", "看重"],
        ["反对", "对抗", "抗争", "抵制", "反抗"],
        ["尽管", "虽然", "即使", "纵然", "哪怕"],
        ["明显", "显著", "清晰", "清楚", "显而易见"],
        ["准确", "精确", "精准", "确切", "正确"],
        ["有效", "奏效", "管用", "高效"],
        ["通常", "一般", "往往", "常常", "经常"]
    ];

    [GeneratedRegex(@"^\s*(?:abbr|pref|suf|comb|contr)\.|^(?:\[[^\]]*\]\s*)?(?:[a-z]+\.\s*)?(?:\[\s*)?(?:古英语|古义|古语|古用法|方言|古(?:\s|\]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonstandardGlossPattern();

    [GeneratedRegex(@"[\u4E00-\u9FFF]", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseCharPattern();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|（[^）]*）", RegexOptions.CultureInvariant)]
    private static partial Regex AnnotationPattern();

    [GeneratedRegex(@"[A-Za-z]+(?:['’-][A-Za-z]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordTokenPattern();

    public static async Task<IReadOnlyList<ContextQuizQuestion>> BuildQuestionsAsync(
        IReadOnlyList<ContextSentenceItem> items,
        LocalDictionary dictionary,
        RelatedWords relatedWords,
        MnemonicStore? mnemonics = null,
        string? accountKey = null,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return [];
        var deckWords = items
            .Select(i => i.Word.Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var questions = new List<ContextQuizQuestion>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var q = await BuildOneAsync(item, deckWords, dictionary, relatedWords, mnemonics, accountKey, cancellationToken);
            questions.Add(q);
        }
        return questions;
    }

    private static async Task<ContextQuizQuestion> BuildOneAsync(
        ContextSentenceItem item,
        string[] deckWords,
        LocalDictionary dictionary,
        RelatedWords relatedWords,
        MnemonicStore? mnemonics,
        string? accountKey,
        CancellationToken cancellationToken)
    {
        var baseWord = item.Word.Trim();
        var baseEntry = dictionary.Lookup(baseWord);
        var exchangeMap = ParseExchange(baseEntry?.exchange);
        var (blankedSentence, answerSurface, inflectionTag, wasCapitalized) = ExtractBlank(item.Sentence, baseWord, item.MatchedForm, exchangeMap);

        var answerMeaningFull = baseEntry?.DisplayMeaning ?? "";
        var answerMeaningCompact = CompactMeaning(answerMeaningFull);
        var answerPos = ExtractPrimaryPos(answerMeaningFull);
        var answerPosGroup = NormalizePosBucket(answerPos);
        var answerAllPosGroups = ExtractAllPosGroups(answerMeaningFull);
        var answerPhonetic = FormatPhonetic(baseEntry?.phonetic);
        var answerSenses = ExtractSenseTokens(answerMeaningFull);
        var slot = AnalyzeBlankSlot(blankedSentence, answerPosGroup, inflectionTag, dictionary);

        // Exclude base word, exact answer surface, and all direct inflections of baseWord from distractors.
        var excludedBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseWord, answerSurface };
        foreach (var form in exchangeMap.Values) excludedBases.Add(form);
        if (!string.IsNullOrWhiteSpace(item.MatchedForm)) excludedBases.Add(item.MatchedForm.Trim());

        var relations = await relatedWords.LookupAsync(baseWord, cancellationToken);
        var isPhraseTarget = baseWord.Contains(' ');

        var synonymCandidates = new List<Candidate>();
        var similarCandidates = new List<Candidate>();
        var derivativeCandidates = new List<Candidate>();

        if (!isPhraseTarget)
        {
            foreach (var rel in relations.Synonyms)
            {
                if (!TryMakeCandidate(rel.Word, rel.Meaning, "近义词", excludedBases, answerMeaningCompact, dictionary, out var c))
                    continue;
                // Exclude same-POS synonyms that could directly substitute for the answer in the blank
                // (so every 4-choice question has a single unambiguous correct answer).
                if (IsInterchangeableSynonym(c, answerMeaningFull, answerPosGroup, answerAllPosGroups))
                    continue;
                synonymCandidates.Add(c);
            }
            foreach (var rel in relations.Similar)
            {
                if (!TryMakeCandidate(rel.Word, rel.Meaning, "形近词", excludedBases, answerMeaningCompact, dictionary, out var c))
                    continue;
                if (IsInterchangeableSynonym(c, answerMeaningFull, answerPosGroup, answerAllPosGroups))
                    continue;
                similarCandidates.Add(c);
            }
            foreach (var rel in relations.Derivatives)
            {
                if (!TryMakeCandidate(rel.Word, rel.Meaning, "派生词", excludedBases, answerMeaningCompact, dictionary, out var c))
                    continue;
                if (IsInterchangeableSynonym(c, answerMeaningFull, answerPosGroup, answerAllPosGroups))
                    continue;
                derivativeCandidates.Add(c);
            }
        }

        // Order synonyms: keep high-relevance single words first; for content words prefer matching primary POS
        // (so verb "adapt" prefers "adjust" over noun "season"), while preserving semantic rank for function words (e.g. "against" -> "oppose").
        var isFunctionTarget = answerPosGroup is "prep" or "conj";
        synonymCandidates = synonymCandidates
            .Select((c, idx) => (Cand: c, Idx: idx))
            .OrderBy(x => x.Cand.BaseWord.Contains(' ') ? 1 : 0)
            .ThenBy(x => isFunctionTarget ? 0 : (x.Cand.PosGroup != answerPosGroup && !x.Cand.AllPosGroups.Contains(answerPosGroup) ? 0 : 1))
            .ThenBy(x => x.Cand.IdenticalGloss ? 1 : 0)
            .ThenBy(x => isFunctionTarget ? x.Idx : x.Idx / 2)
            .ThenBy(x => dictionary.FrequencyRank(x.Cand.BaseWord))
            .Select(x => x.Cand)
            .ToList();
        derivativeCandidates = derivativeCandidates
            .OrderBy(c => NormalizePosBucket(c.PrimaryPos) != answerPosGroup && c.PrimaryPos.Length > 0 ? 0 : 1)
            .ThenBy(c => dictionary.FrequencyRank(c.BaseWord))
            .ToList();

        var chosen = new List<Candidate>(3);
        var usedDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { answerSurface };
        var usedBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseWord };

        bool TryPickFrom(List<Candidate> pool)
        {
            foreach (var cand in pool)
            {
                if (usedBases.Contains(cand.BaseWord)) continue;
                var display = FormatDistractorSurface(cand, inflectionTag, wasCapitalized, answerPosGroup);
                if (!usedDisplays.Add(display)) continue;
                usedBases.Add(cand.BaseWord);
                chosen.Add(cand with { DisplayText = display });
                return true;
            }
            return false;
        }

        // Round 1: balanced 1+1+1 trio (prioritizing 近义词 first, then 形近词 + 派生词)
        TryPickFrom(synonymCandidates);
        TryPickFrom(similarCandidates);
        TryPickFrom(derivativeCandidates);

        // Round 2: if any bucket was missing, prioritize 近义词 first for remaining slots, then 形近词, then 派生词
        while (chosen.Count < 3 && TryPickFrom(synonymCandidates)) { }
        for (var guard = 0; chosen.Count < 3 && guard < 6; guard++)
        {
            var before = chosen.Count;
            if (chosen.Count < 3) TryPickFrom(similarCandidates);
            if (chosen.Count < 3) TryPickFrom(derivativeCandidates);
            if (chosen.Count == before) break;
        }

        // Round 3: spelling-distance fallback from full dictionary
        if (chosen.Count < 3 && !isPhraseTarget)
        {
            foreach (var spelling in dictionary.SuggestSpellingCorrections(baseWord, 10))
            {
                if (chosen.Count >= 3) break;
                if (!TryMakeCandidate(spelling, null, "形近词", excludedBases, answerMeaningCompact, dictionary, out var c)) continue;
                if (IsInterchangeableSynonym(c, answerMeaningFull, answerPosGroup, answerAllPosGroups)) continue;
                if (usedBases.Contains(c.BaseWord)) continue;
                var display = FormatDistractorSurface(c, inflectionTag, wasCapitalized, answerPosGroup);
                if (!usedDisplays.Add(display)) continue;
                usedBases.Add(c.BaseWord);
                chosen.Add(c with { DisplayText = display });
            }
        }

        // Round 4: other words from the user's saved sentence deck
        if (chosen.Count < 3)
        {
            foreach (var other in deckWords.OrderBy(_ => Random.Shared.Next()))
            {
                if (chosen.Count >= 3) break;
                if (other.Contains(' ') && !isPhraseTarget) continue;
                if (!TryMakeCandidate(other, null, "已收藏词", excludedBases, answerMeaningCompact, dictionary, out var c)) continue;
                if (IsInterchangeableSynonym(c, answerMeaningFull, answerPosGroup, answerAllPosGroups)) continue;
                if (usedBases.Contains(c.BaseWord)) continue;
                var display = FormatDistractorSurface(c, inflectionTag, wasCapitalized, answerPosGroup);
                if (!usedDisplays.Add(display)) continue;
                usedBases.Add(c.BaseWord);
                chosen.Add(c with { DisplayText = display });
            }
        }

        // Round 5: high-frequency academic fallback matching target POS (guaranteed 4 options)
        if (chosen.Count < 3)
        {
            var fallbackOrder = new[] { answerPosGroup, "v", "n", "adj", "adv", "prep" }.Distinct();
            foreach (var key in fallbackOrder)
            {
                if (chosen.Count >= 3) break;
                if (!FallbackPools.TryGetValue(key, out var words)) continue;
                foreach (var word in words.OrderBy(_ => Random.Shared.Next()))
                {
                    if (chosen.Count >= 3) break;
                    if (!TryMakeCandidate(word, null, "高频辨析", excludedBases, answerMeaningCompact, dictionary, out var c)) continue;
                    if (IsInterchangeableSynonym(c, answerMeaningFull, answerPosGroup, answerAllPosGroups)) continue;
                    if (usedBases.Contains(c.BaseWord)) continue;
                    var display = FormatDistractorSurface(c, inflectionTag, wasCapitalized, answerPosGroup);
                    if (!usedDisplays.Add(display)) continue;
                    usedBases.Add(c.BaseWord);
                    chosen.Add(c with { DisplayText = display });
                }
            }
        }

        // Build contrastive option analyses and shuffle A/B/C/D
        var finalDistractors = chosen.Take(3).ToArray();
        var rawOptions = new List<ContextQuizOption>(4);
        var answerNote = BuildCorrectAnalysis(answerSurface, baseWord, answerPos, answerPosGroup, answerMeaningFull, inflectionTag, slot);
        rawOptions.Add(new ContextQuizOption(
            "",
            answerSurface,
            baseWord,
            answerPhonetic,
            answerMeaningCompact.Length > 0 ? answerMeaningCompact : "目标词汇",
            "正确答案",
            answerNote,
            IsCorrect: true));

        foreach (var cand in finalDistractors)
        {
            rawOptions.Add(new ContextQuizOption(
                "",
                cand.DisplayText,
                cand.BaseWord,
                cand.Phonetic,
                cand.CompactMeaning,
                cand.Kind,
                BuildDistractorAnalysis(cand, baseWord, answerPos, answerPosGroup, answerMeaningFull, answerSenses, slot),
                IsCorrect: false));
        }

        // Fisher-Yates shuffle
        for (var i = rawOptions.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (rawOptions[i], rawOptions[j]) = (rawOptions[j], rawOptions[i]);
        }

        var labeledOptions = rawOptions
            .Select((opt, idx) => opt with { Label = idx < OptionLabels.Length ? OptionLabels[idx] : ((char)('A' + idx)).ToString() })
            .ToArray();

        string? mnemonicHint = null;
        if (mnemonics != null)
        {
            var firstNote = mnemonics.Get(baseWord, accountKey).FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(firstNote))
            {
                var cleanNote = Regex.Replace(firstNote.Trim(), @"\s+", " ");
                mnemonicHint = cleanNote.Length <= 120 ? cleanNote : cleanNote[..117] + "…";
            }
        }

        var summary = BuildExplanationSummary(baseWord, answerSurface, answerPosGroup, answerMeaningFull, inflectionTag, slot, finalDistractors);

        return new ContextQuizQuestion(
            item.Id,
            baseWord,
            string.IsNullOrWhiteSpace(item.Source) ? "我的例句" : item.Source,
            blankedSentence,
            item.Sentence,
            item.Translation,
            answerSurface,
            baseWord,
            answerPhonetic,
            answerMeaningCompact.Length > 0 ? answerMeaningCompact : "暂无内置释义",
            mnemonicHint,
            summary,
            labeledOptions);
    }

    private sealed record BlankSlotInfo(
        string? LeftWord,
        string? RightWord,
        string CollocationQuote,
        string GrammarCue);

    private sealed record Candidate(
        string BaseWord,
        string DisplayText,
        string Phonetic,
        string FullMeaning,
        string CompactMeaning,
        string PrimaryPos,
        string PosGroup,
        HashSet<string> AllPosGroups,
        string[] SenseTokens,
        string Kind,
        Dictionary<string, string> Exchange,
        bool IdenticalGloss);

    private static bool TryMakeCandidate(
        string rawWord,
        string? suppliedMeaning,
        string kind,
        HashSet<string> excludedBases,
        string targetCompactMeaning,
        LocalDictionary dictionary,
        out Candidate candidate)
    {
        candidate = default!;
        var word = (rawWord ?? "").Trim();
        if (word.Length is < 2 or > 40 || excludedBases.Contains(word)) return false;
        if (!char.IsAsciiLetter(word[0])) return false;

        var entry = dictionary.Lookup(word);
        if (entry != null && (entry.HasHistoricalDefinition || NonstandardGlossPattern().IsMatch(entry.translation)))
            return false;

        var rawMeaning = !string.IsNullOrWhiteSpace(entry?.DisplayMeaning)
            ? entry.DisplayMeaning
            : (suppliedMeaning ?? "");
        if (string.IsNullOrWhiteSpace(rawMeaning) || !ChineseCharPattern().IsMatch(rawMeaning))
            return false;
        if (NonstandardGlossPattern().IsMatch(rawMeaning)) return false;

        var compact = CompactMeaning(rawMeaning);
        if (compact.Length == 0) return false;

        var pos = ExtractPrimaryPos(rawMeaning);
        var posGroup = NormalizePosBucket(pos);
        var allPos = ExtractAllPosGroups(rawMeaning);
        var exchange = ParseExchange(entry?.exchange);
        var senses = ExtractSenseTokens(rawMeaning);
        var identicalGloss = targetCompactMeaning.Length > 0
            && string.Equals(StripPos(compact), StripPos(targetCompactMeaning), StringComparison.OrdinalIgnoreCase);

        candidate = new Candidate(
            entry?.word ?? word,
            entry?.word ?? word,
            FormatPhonetic(entry?.phonetic),
            rawMeaning,
            compact,
            pos,
            posGroup,
            allPos,
            senses,
            kind,
            exchange,
            identicalGloss);
        return true;
    }

    private static BlankSlotInfo AnalyzeBlankSlot(string blankedSentence, string answerPosGroup, string inflectionTag, LocalDictionary dictionary)
    {
        var idx = blankedSentence.IndexOf("______", StringComparison.Ordinal);
        if (idx < 0) return new BlankSlotInfo(null, null, "", "");

        var before = blankedSentence[..idx];
        var after = blankedSentence[(idx + 6)..];
        var leftMatches = WordTokenPattern().Matches(before);
        var rightMatches = WordTokenPattern().Matches(after);
        var leftWord = leftMatches.Count > 0 ? leftMatches[^1].Value : null;
        var rightWord = rightMatches.Count > 0 ? rightMatches[0].Value : null;

        var leftSnippet = leftMatches.Count >= 2 ? $"{leftMatches[^2].Value} {leftMatches[^1].Value}" : leftWord ?? "";
        // Extend right phrase to 3 words when the first word is a determiner (e.g. "the severe storm" / "the new highway")
        var rightTake = rightMatches.Count >= 3 && rightWord != null && DeterminerCue.Contains(rightWord) ? 3 : Math.Min(2, rightMatches.Count);
        var rightSnippet = string.Join(" ", Enumerable.Range(0, rightTake).Select(i => rightMatches[i].Value));
        var quote = string.Join(" ", new[] { leftSnippet, "______", rightSnippet }.Where(s => s.Length > 0));
        var collocationQuote = quote.Length > 0 ? $"「{quote}」" : "";

        string cue = "";
        if (answerPosGroup == "prep")
        {
            cue = rightWord != null
                ? $"此处需用介词引出宾语「{rightSnippet}」"
                : "此处需用介词连接上下文语义关系";
        }
        else if (answerPosGroup == "conj")
        {
            cue = "此处需用连词衔接前后分句逻辑";
        }
        else if (leftWord != null && ModalOrInfinitiveCue.Contains(leftWord) && answerPosGroup == "v")
        {
            cue = $"「{leftWord}」后需接动词原形";
        }
        else if (leftWord != null && BeOrAuxCue.Contains(leftWord) && inflectionTag is "d" or "p" or "i")
        {
            var formName = InflectionLabel(inflectionTag);
            cue = $"助动词/系动词「{leftWord}」后搭配{formName}构成谓语";
        }
        else if (answerPosGroup == "adj" && rightWord != null)
        {
            var rightPos = NormalizePosBucket(ExtractPrimaryPos(dictionary.Lookup(rightWord)?.DisplayMeaning));
            cue = rightPos == "n"
                ? $"修饰后面的名词「{rightWord}」需用形容词"
                : leftWord != null && BeOrAuxCue.Contains(leftWord)
                    ? $"系动词「{leftWord}」后需接形容词作表语"
                    : "此处需用形容词修饰限定";
        }
        else if (answerPosGroup == "n" && leftWord != null)
        {
            if (DeterminerCue.Contains(leftWord))
                cue = $"限定词「{leftWord}」后需接名词成分";
            else
            {
                var leftPos = NormalizePosBucket(ExtractPrimaryPos(dictionary.Lookup(leftWord)?.DisplayMeaning));
                cue = leftPos == "adj"
                    ? $"形容词「{leftWord}」后需接名词作中心语"
                    : "此处在句中充当名词成分";
            }
        }
        else if (answerPosGroup == "v")
        {
            var tagLabel = InflectionLabel(inflectionTag);
            if (leftWord != null && leftWord.EndsWith("ly", StringComparison.OrdinalIgnoreCase))
                cue = tagLabel.Length > 0 ? $"副词「{leftWord}」后修饰动词（此处用{tagLabel}）" : $"副词「{leftWord}」后需接动词";
            else if (tagLabel.Length > 0)
                cue = $"此处需用动词的{tagLabel}形式";
            else
                cue = "此处需填动词作谓语或动作成分";
        }
        else if (answerPosGroup == "adv")
        {
            cue = rightWord != null ? $"修饰「{rightWord}」或整句，需用副词" : "此处需用副词修饰动作或程度";
        }

        return new BlankSlotInfo(leftWord, rightWord, collocationQuote, cue);
    }

    private static (string Blanked, string Surface, string InflectionTag, bool Capitalized) ExtractBlank(
        string sentence,
        string baseWord,
        string? matchedForm,
        Dictionary<string, string> exchangeMap)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return ("______", baseWord, "", false);

        var cleanBase = baseWord.Trim();
        var cleanMatched = string.IsNullOrWhiteSpace(matchedForm) ? null : matchedForm.Trim();

        if (cleanBase.Contains(' '))
        {
            var targets = cleanMatched != null && !cleanMatched.Equals(cleanBase, StringComparison.OrdinalIgnoreCase)
                ? new[] { cleanMatched, cleanBase }
                : new[] { cleanBase };
            foreach (var target in targets)
            {
                var pattern = $@"\b{Regex.Escape(target)}\b";
                var m = Regex.Match(sentence, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (m.Success)
                {
                    var surface = m.Value;
                    var blanked = Regex.Replace(sentence, pattern, "______", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    return (blanked, surface, "", IsCapitalizedWord(surface));
                }
            }
            return (ContextSentenceHelper.BlankOutWord(sentence, cleanBase, cleanMatched), cleanMatched ?? cleanBase, "", false);
        }

        var formToTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ExampleText.NormalizeLookup(cleanBase)] = ""
        };
        foreach (var (tag, form) in exchangeMap)
        {
            var norm = ExampleText.NormalizeLookup(form);
            if (norm.Length > 0 && !formToTag.ContainsKey(norm))
                formToTag[norm] = tag;
        }
        if (cleanMatched != null)
        {
            var normMatched = ExampleText.NormalizeLookup(cleanMatched);
            if (normMatched.Length > 0 && !formToTag.ContainsKey(normMatched))
                formToTag[normMatched] = GuessInflectionTag(cleanBase, cleanMatched);
        }

        var parts = ExampleText.Tokenize(sentence);
        var matchedIndexes = new List<int>();
        string? foundSurface = null;
        var foundTag = "";

        for (var i = 0; i < parts.Count; i++)
        {
            var lw = parts[i].LookupWord;
            if (lw != null && formToTag.TryGetValue(lw, out var tag))
            {
                matchedIndexes.Add(i);
                if (foundSurface == null)
                {
                    foundSurface = parts[i].Text;
                    foundTag = tag;
                }
            }
        }

        if (matchedIndexes.Count == 0 && cleanBase.Length >= 4)
        {
            var stem = cleanBase.ToLowerInvariant();
            if (stem.Length > 4 && stem.EndsWith('e')) stem = stem[..^1];
            for (var i = 0; i < parts.Count; i++)
            {
                var lw = parts[i].LookupWord;
                if (lw != null && lw.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                {
                    matchedIndexes.Add(i);
                    foundSurface = parts[i].Text;
                    foundTag = GuessInflectionTag(cleanBase, foundSurface);
                    break;
                }
            }
        }

        if (matchedIndexes.Count == 0 || foundSurface == null)
        {
            var fallbackSurface = cleanMatched ?? cleanBase;
            return (sentence.TrimEnd() + " (______)", fallbackSurface, GuessInflectionTag(cleanBase, fallbackSurface), false);
        }

        var matchedSet = matchedIndexes.ToHashSet();
        var sb = new StringBuilder(sentence.Length + 8);
        for (var i = 0; i < parts.Count; i++)
        {
            sb.Append(matchedSet.Contains(i) ? "______" : parts[i].Text);
        }
        return (sb.ToString(), foundSurface, foundTag, IsCapitalizedWord(foundSurface));
    }

    private static string FormatDistractorSurface(Candidate cand, string inflectionTag, bool capitalize, string targetPosGroup)
    {
        var surface = cand.BaseWord;

        // Only align inflections when the distractor can legitimately take that inflection
        // (avoid mangling cross-POS or derivative distractors).
        if (inflectionTag.Length > 0 && !cand.BaseWord.Contains(' '))
        {
            var sameFamily = cand.PosGroup == targetPosGroup || cand.Exchange.ContainsKey(inflectionTag);
            var shouldInflect = cand.Kind != "派生词"
                ? sameFamily
                : (inflectionTag == "s" && cand.PosGroup == "n");
            if (shouldInflect)
            {
                if (cand.Exchange.TryGetValue(inflectionTag, out var inflected) && inflected.Length > 0)
                    surface = inflected;
                else if (inflectionTag == "p" && cand.Exchange.TryGetValue("d", out var part) && part.Length > 0)
                    surface = part;
                else if (inflectionTag == "d" && cand.Exchange.TryGetValue("p", out var past) && past.Length > 0)
                    surface = past;
            }
        }

        return ApplyCapitalization(surface, capitalize);
    }

    private static string BuildCorrectAnalysis(
        string answerSurface,
        string baseWord,
        string pos,
        string posGroup,
        string fullMeaning,
        string inflectionTag,
        BlankSlotInfo slot)
    {
        var sb = new StringBuilder();
        var posName = PosChineseName(posGroup);
        var brief = BriefPrimarySense(fullMeaning);
        if (!answerSurface.Equals(baseWord, StringComparison.OrdinalIgnoreCase))
        {
            var tagLabel = InflectionLabel(inflectionTag);
            sb.Append(tagLabel.Length > 0 ? $"原形 {baseWord}（{tagLabel}），" : $"原形 {baseWord}，");
        }
        else if (posName.Length > 0)
        {
            sb.Append($"作{posName}（{pos}），");
        }

        if (brief.Length > 0)
            sb.Append($"核心义为「{brief}」。");
        if (slot.GrammarCue.Length > 0)
            sb.Append(slot.GrammarCue);
        else if (slot.CollocationQuote.Length > 0)
            sb.Append($"在搭配 {slot.CollocationQuote} 中");
        else
            sb.Append("代入原句后");
        sb.Append("，语义与语法结构均最贴合。");
        return sb.ToString();
    }

    private static string BuildDistractorAnalysis(
        Candidate cand,
        string baseWord,
        string targetPos,
        string targetPosGroup,
        string targetFullMeaning,
        string[] targetSenses,
        BlankSlotInfo slot)
    {
        var formNote = !cand.DisplayText.Equals(cand.BaseWord, StringComparison.OrdinalIgnoreCase)
            ? $"（原形 {cand.BaseWord}）"
            : "";
        var canBeTargetPos = targetPosGroup.Length > 0 && cand.AllPosGroups.Contains(targetPosGroup);
        var strictPosDiff = cand.PosGroup.Length > 0 && targetPosGroup.Length > 0 && !canBeTargetPos;
        var primaryPosDiff = cand.PosGroup.Length > 0 && targetPosGroup.Length > 0 && cand.PosGroup != targetPosGroup;
        var candPosName = PosChineseName(cand.PosGroup);
        var targetPosName = PosChineseName(targetPosGroup);
        var candCore = BriefPrimarySense(cand.FullMeaning, canBeTargetPos ? targetPosGroup : null);
        var targetCore = BriefPrimarySense(targetFullMeaning);

        var sharedSenses = FindSharedSenses(targetSenses, cand.SenseTokens);
        var candUnique = cand.SenseTokens.Where(s => !sharedSenses.Contains(s)).Take(2).ToArray();
        var targetUnique = targetSenses.Where(s => !sharedSenses.Contains(s)).Take(2).ToArray();

        if (cand.Kind == "近义词")
        {
            if (strictPosDiff && sharedSenses.Count > 0)
            {
                var sharedStr = string.Join("、", sharedSenses.Take(2));
                var grammarNote = slot.GrammarCue.Length > 0 ? slot.GrammarCue : $"本空需要{targetPosName}";
                return $"近义但词性不同{formNote}：与 {baseWord} 都含「{sharedStr}」之意，极易混用；但 {cand.BaseWord} 是{candPosName}（{cand.PrimaryPos} {candCore}），{grammarNote}，不能直接替换。";
            }
            if (strictPosDiff)
            {
                return $"近义词性辨析{formNote}：语义与 {baseWord} 相关（{cand.PrimaryPos} {candCore}），但 {cand.BaseWord} 为{candPosName}；本空需{targetPosName}成分，语法功能不匹配。";
            }
            if (sharedSenses.Count > 0 && (candUnique.Length > 0 || targetUnique.Length > 0))
            {
                var sharedStr = string.Join("、", sharedSenses.Take(2));
                var candFocus = candUnique.Length > 0 ? string.Join("、", candUnique) : candCore;
                var targetFocus = targetUnique.Length > 0 ? string.Join("、", targetUnique) : targetCore;
                var ctx = slot.CollocationQuote.Length > 0 ? $"结合 {slot.CollocationQuote}，" : "结合本句语境，";
                return $"同词性近义辨析{formNote}：两者都涉及「{sharedStr}」，但 {cand.BaseWord} 偏重「{candFocus}」，而 {baseWord} 更强调「{targetFocus}」；{ctx}选 {baseWord} 更精准。";
            }
            var ctxHint = slot.CollocationQuote.Length > 0 ? $"在 {slot.CollocationQuote} 中" : "在当前语境下";
            return $"近义侧重辨析{formNote}：{cand.BaseWord} 侧重「{candCore}」，而 {baseWord} 强调「{targetCore}」；{ctxHint}语义与搭配不如 {baseWord} 贴切。";
        }

        if (cand.Kind == "形近词")
        {
            var diffTip = DescribeSpellingDiff(baseWord, cand.BaseWord);
            var posTip = strictPosDiff && candPosName.Length > 0 && targetPosName.Length > 0
                ? $"，且 {cand.BaseWord} 为{candPosName}（本空需{targetPosName}）"
                : primaryPosDiff && candPosName.Length > 0 && targetPosName.Length > 0
                    ? $"，且 {cand.BaseWord} 常用作{candPosName}（作{targetPosName}时义项不同）"
                    : "";
            if (sharedSenses.Count > 0)
            {
                var sharedStr = string.Join("、", sharedSenses.Take(2));
                return $"高危形近辨析{formNote}：{diffTip}，且都易与「{sharedStr}」联想；但 {cand.BaseWord} 实指「{cand.PrimaryPos} {candCore}」{posTip}，切勿看混。";
            }
            return $"形近拼写辨析{formNote}：{diffTip}，外形极易混淆；但 {cand.BaseWord} 意为「{cand.PrimaryPos} {candCore}」{posTip}，与本句所需「{targetCore}」语义无关。";
        }

        if (cand.Kind == "派生词")
        {
            var suffixTip = DescribeSuffixContrast(baseWord, cand.BaseWord);
            if (strictPosDiff)
            {
                var slotTip = slot.GrammarCue.Length > 0 ? slot.GrammarCue : $"本空需填{targetPosName}";
                return $"同根派生词性辨析{formNote}：与 {baseWord} 同源，{suffixTip}变为{candPosName}（{cand.PrimaryPos} {candCore}）；但{slotTip}，词性不符。";
            }
            return $"同根派生词义辨析{formNote}：与 {baseWord} 同根，{suffixTip}语义偏向「{candCore}」，而本句需要表达「{targetCore}」，故不选。";
        }

        if (strictPosDiff && candPosName.Length > 0 && targetPosName.Length > 0)
            return $"词性与语义排除{formNote}：{cand.BaseWord} 为{candPosName}（{cand.PrimaryPos} {candCore}），既与本空所需的{targetPosName}不匹配，也与句意「{targetCore}」相悖。";
        return $"语境语义排除{formNote}：{cand.BaseWord} 意为「{candCore}」，代入句中与原意「{targetCore}」不符。";
    }

    private static string BuildExplanationSummary(
        string baseWord,
        string answerSurface,
        string answerPosGroup,
        string fullMeaning,
        string inflectionTag,
        BlankSlotInfo slot,
        Candidate[] distractors)
    {
        var posName = PosChineseName(answerPosGroup);
        var core = BriefPrimarySense(fullMeaning);
        var tagLabel = InflectionLabel(inflectionTag);

        var hasSyn = distractors.FirstOrDefault(d => d.Kind == "近义词");
        var hasSim = distractors.FirstOrDefault(d => d.Kind == "形近词");
        var hasDer = distractors.FirstOrDefault(d => d.Kind == "派生词");

        var sb = new StringBuilder();
        sb.Append("【辨析要点】");
        if (slot.CollocationQuote.Length > 0)
            sb.Append($"观察空格前后 {slot.CollocationQuote}，");
        if (slot.GrammarCue.Length > 0)
            sb.Append($"{slot.GrammarCue}，");
        else if (posName.Length > 0)
            sb.Append($"此处需用{posName}，");

        if (tagLabel.Length > 0 && !answerSurface.Equals(baseWord, StringComparison.OrdinalIgnoreCase))
            sb.Append($"应填入 {baseWord}（{core}）的{tagLabel}「{answerSurface}」。");
        else
            sb.Append($"表达「{core}」，故选「{answerSurface}」。");

        var traps = new List<string>(2);
        if (hasSyn != null)
        {
            var synStrictPosDiff = hasSyn.PosGroup.Length > 0 && answerPosGroup.Length > 0 && !hasSyn.AllPosGroups.Contains(answerPosGroup);
            traps.Add(synStrictPosDiff
                ? $"同义异词性词 {hasSyn.BaseWord}（{PosChineseName(hasSyn.PosGroup)}）"
                : $"近义词 {hasSyn.BaseWord} 的语境侧重差异");
        }
        if (hasSim != null)
            traps.Add($"形近词 {hasSim.BaseWord}（{DescribeSpellingDiffShort(baseWord, hasSim.BaseWord)}）");
        else if (hasDer != null)
            traps.Add($"同根派生词 {hasDer.BaseWord} 的词性变化");

        if (traps.Count > 0)
            sb.Append("重点区分").Append(string.Join("，以及", traps)).Append('。');

        return sb.ToString();
    }

    private static string DescribeSpellingDiff(string target, string other)
    {
        var a = target.ToLowerInvariant();
        var b = other.ToLowerInvariant();
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;

        var diffA = a.Substring(prefix, a.Length - prefix - suffix);
        var diffB = b.Substring(prefix, b.Length - prefix - suffix);

        if (prefix == 0 && suffix >= 2)
            return $"首部「{(diffA.Length > 0 ? diffA : "无")} / {(diffB.Length > 0 ? diffB : "无")}」之差（{target} ↔ {other}）";
        if (suffix == 0 && prefix >= 2)
            return $"词尾「{(diffA.Length > 0 ? "-" + diffA : "无")} / {(diffB.Length > 0 ? "-" + diffB : "无")}」之差（{target} ↔ {other}）";
        if (diffA.Length <= 2 && diffB.Length <= 2 && prefix > 0 && suffix > 0)
        {
            var markA = $"{a[..prefix]}[{diffA}]{a[(a.Length - suffix)..]}";
            var markB = $"{b[..prefix]}[{diffB}]{b[(b.Length - suffix)..]}";
            return $"中间字母仅一处之差（{markA} ↔ {markB}）";
        }
        return $"拼写高度相近（{target} ↔ {other}）";
    }

    private static string DescribeSpellingDiffShort(string target, string other)
    {
        var a = target.ToLowerInvariant();
        var b = other.ToLowerInvariant();
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;
        var diffA = a.Substring(prefix, a.Length - prefix - suffix);
        var diffB = b.Substring(prefix, b.Length - prefix - suffix);
        if (diffA.Length is > 0 and <= 2 && diffB.Length is > 0 and <= 2)
            return $"{diffA}/{diffB} 之差";
        if (suffix == 0 && prefix >= 2)
            return diffA.Length > 0 ? $"词尾差 -{diffA}" : $"词尾多 -{diffB}";
        return "拼写易混";
    }

    private static string DescribeSuffixContrast(string baseWord, string derived)
    {
        var low = derived.ToLowerInvariant();
        var baseLow = baseWord.ToLowerInvariant();
        foreach (var (suffix, posHint, meaningHint) in KnownSuffixes)
        {
            if (low.EndsWith(suffix, StringComparison.Ordinal) && !baseLow.EndsWith(suffix, StringComparison.Ordinal) && low.Length > suffix.Length + 2)
                return $"后缀 -{suffix}（{meaningHint}）使其";
        }
        return "词形派生后";
    }

    private static readonly HashSet<string> UnmarkedVerbHomographs = new(StringComparer.OrdinalIgnoreCase)
    {
        "better", "further", "narrow", "lower", "clear", "calm", "empty", "clean", "open", "close", "free", "dry", "warm", "cool", "slow"
    };

    private static bool IsInterchangeableSynonym(
        Candidate cand,
        string targetFullMeaning,
        string targetPosGroup,
        HashSet<string> targetAllPosGroups)
    {
        var sharedPos = targetAllPosGroups.Where(p => cand.AllPosGroups.Contains(p)).ToList();
        if (sharedPos.Count == 0 && targetPosGroup.Length > 0 && cand.AllPosGroups.Contains(targetPosGroup))
            sharedPos.Add(targetPosGroup);

        if (cand.IdenticalGloss && (sharedPos.Count > 0 || targetAllPosGroups.Count == 0))
            return true;

        // Cross-POS synonyms (e.g. prep. "against" vs vt. "oppose", prep. "despite" vs conj. "although")
        // cannot grammatically substitute for each other, so they are unambiguous, high-value distractors.
        var isUnmarkedVerbHomograph = cand.Kind == "近义词"
            && targetAllPosGroups.Contains("v")
            && UnmarkedVerbHomographs.Contains(cand.BaseWord);

        if (sharedPos.Count == 0 && targetAllPosGroups.Count > 0 && !isUnmarkedVerbHomograph)
            return false;

        var posScopes = sharedPos.Count > 0 ? sharedPos : [""];
        foreach (var pos in posScopes)
        {
            var targetSenses = pos.Length > 0
                ? ExtractMatchingPosSenses(targetFullMeaning, pos)
                : ExtractSenseTokens(targetFullMeaning);
            var candSenses = pos.Length > 0
                ? ExtractMatchingPosSenses(cand.FullMeaning, pos)
                : ExtractSenseTokens(cand.FullMeaning);
            if (targetSenses.Length == 0 || candSenses.Length == 0) continue;

            var targetLead = targetSenses.Take(3).ToArray();
            var candLead = candSenses.Take(3).ToArray();

            foreach (var t in targetLead)
            {
                foreach (var c in candLead)
                {
                    if (t == c) return true;
                    if (t == targetLead[0] && c == candLead[0] && t.Length >= 2 && c.Length >= 2 &&
                        (t.Contains(c, StringComparison.Ordinal) || c.Contains(t, StringComparison.Ordinal)))
                        return true;
                    if (SharesInterchangeableGroup(t, c))
                        return true;
                }
            }

            var primary = targetLead[0];
            if (primary.Length >= 2 && candSenses.Any(s => s == primary || SharesInterchangeableGroup(primary, s)))
                return true;
        }

        if (isUnmarkedVerbHomograph)
        {
            var targetAllSenses = ExtractSenseTokens(targetFullMeaning);
            var candAllSenses = ExtractSenseTokens(cand.FullMeaning);
            foreach (var t in targetAllSenses)
            {
                foreach (var c in candAllSenses)
                {
                    if (t == c || SharesInterchangeableGroup(t, c))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool SharesInterchangeableGroup(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return false;
        foreach (var group in InterchangeableChineseGroups)
        {
            var hasA = false;
            var hasB = false;
            foreach (var term in group)
            {
                if (!hasA && (a == term || a.Contains(term, StringComparison.Ordinal))) hasA = true;
                if (!hasB && (b == term || b.Contains(term, StringComparison.Ordinal))) hasB = true;
                if (hasA && hasB) return true;
            }
        }
        return false;
    }

    private static string[] ExtractMatchingPosSenses(string? displayMeaning, string targetPosGroup)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return [];
        var lines = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var pos = NormalizePosBucket(DictionaryEntry.SplitDisplaySense(line).PartOfSpeech.Trim());
            if (targetPosGroup.Length > 0 && pos.Length > 0 && pos != targetPosGroup) continue;
            foreach (var token in ExtractSensesFromLine(line))
            {
                if (seen.Add(token)) list.Add(token);
            }
        }
        if (list.Count == 0 && lines.Length > 0)
            return ExtractSensesFromLine(lines[0]);
        return list.ToArray();
    }

    private static string[] ExtractSensesFromLine(string line)
    {
        var (_, rest) = DictionaryEntry.SplitDisplaySense(line);
        var cleaned = AnnotationPattern().Replace(rest.Length > 0 ? rest : line, "");
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var piece in cleaned.Split(['，', ',', '；', ';', '、', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = piece.Trim();
            if (token.Length > 2 && (token.EndsWith('的') || token.EndsWith('地'))) token = token[..^1];
            if (token.Length >= 2 && ChineseCharPattern().IsMatch(token) && seen.Add(token))
                list.Add(token);
        }
        return list.ToArray();
    }

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

    private static HashSet<string> FindSharedSenses(string[] targetSenses, string[] candSenses)
    {
        var shared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in targetSenses)
        {
            foreach (var c in candSenses)
            {
                if (t == c) { shared.Add(t); continue; }
                if (t.Length >= 2 && c.Length >= 2 && (t.Contains(c, StringComparison.Ordinal) || c.Contains(t, StringComparison.Ordinal)))
                    shared.Add(t.Length <= c.Length ? t : c);
            }
        }
        return shared;
    }

    private static Dictionary<string, string> ParseExchange(string? exchange)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(exchange)) return map;
        foreach (var piece in exchange.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = piece.IndexOf(':');
            if (colon <= 0 || colon >= piece.Length - 1) continue;
            var tag = piece[..colon].Trim();
            if (tag == "1") continue;
            var form = piece[(colon + 1)..].Trim();
            if (form.Length > 0 && !map.ContainsKey(tag))
                map[tag] = form;
        }
        return map;
    }

    private static string GuessInflectionTag(string baseWord, string surface)
    {
        var b = baseWord.Trim().ToLowerInvariant();
        var s = surface.Trim().ToLowerInvariant();
        if (b == s) return "";
        if (s.EndsWith("ing", StringComparison.Ordinal) && !b.EndsWith("ing", StringComparison.Ordinal)) return "i";
        if (s.EndsWith("ed", StringComparison.Ordinal) && !b.EndsWith("ed", StringComparison.Ordinal)) return "p";
        if (s.EndsWith("es", StringComparison.Ordinal) || (s.EndsWith('s') && !b.EndsWith('s'))) return "s";
        if (s.EndsWith("er", StringComparison.Ordinal) && !b.EndsWith("er", StringComparison.Ordinal)) return "r";
        if (s.EndsWith("est", StringComparison.Ordinal) && !b.EndsWith("est", StringComparison.Ordinal)) return "t";
        return "";
    }

    private static string InflectionLabel(string tag) => tag switch
    {
        "p" => "过去式",
        "d" => "过去分词",
        "i" => "现在分词",
        "3" => "第三人称单数",
        "s" => "复数/三单",
        "r" => "比较级",
        "t" => "最高级",
        _ => ""
    };

    private static string PosChineseName(string posGroup) => posGroup switch
    {
        "v" => "动词",
        "n" => "名词",
        "adj" => "形容词",
        "adv" => "副词",
        "prep" => "介词",
        "conj" => "连词",
        _ => ""
    };

    private static bool IsCapitalizedWord(string text) =>
        text.Length > 0 && char.IsUpper(text[0]) && (text.Length == 1 || text.Skip(1).Any(char.IsLower));

    private static string ApplyCapitalization(string text, bool capitalize)
    {
        if (!capitalize || text.Length == 0 || char.IsUpper(text[0])) return text;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string FormatPhonetic(string? phonetic)
    {
        if (string.IsNullOrWhiteSpace(phonetic)) return "";
        return "/" + phonetic.Trim().Trim('/') + "/";
    }

    private static string CompactMeaning(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var lines = displayMeaning
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join("；", lines.Take(2));
        return joined.Length <= 56 ? joined : joined[..54].TrimEnd('；', '，', '、', ' ') + "…";
    }

    private static string ExtractPrimaryPos(string? displayMeaning)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var first = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return DictionaryEntry.SplitDisplaySense(first).PartOfSpeech.Trim();
    }

    private static HashSet<string> ExtractAllPosGroups(string? displayMeaning)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(displayMeaning)) return set;
        foreach (var line in displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var group = NormalizePosBucket(DictionaryEntry.SplitDisplaySense(line).PartOfSpeech.Trim());
            if (group.Length > 0) set.Add(group);
        }
        return set;
    }

    private static string BriefPrimarySense(string? displayMeaning, string? preferPosGroup = null)
    {
        if (string.IsNullOrWhiteSpace(displayMeaning)) return "";
        var lines = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string chosen = lines[0];
        if (!string.IsNullOrEmpty(preferPosGroup))
        {
            var matched = lines.FirstOrDefault(l => NormalizePosBucket(DictionaryEntry.SplitDisplaySense(l).PartOfSpeech.Trim()) == preferPosGroup);
            if (matched != null) chosen = matched;
        }
        var (_, rest) = DictionaryEntry.SplitDisplaySense(chosen);
        var cleaned = AnnotationPattern().Replace(rest.Length > 0 ? rest : chosen, "");
        var pieces = cleaned
            .Split(['；', ';', '，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        var joined = pieces.Length > 0 ? string.Join("、", pieces) : StripPos(CompactMeaning(displayMeaning));
        return joined.Length <= 24 ? joined : joined[..22].TrimEnd('、', '，', '；', ' ') + "…";
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

    private static string StripPos(string meaning)
    {
        var (pos, rest) = DictionaryEntry.SplitDisplaySense(meaning);
        return pos.Length > 0 ? rest.Trim() : meaning.Trim();
    }
}
