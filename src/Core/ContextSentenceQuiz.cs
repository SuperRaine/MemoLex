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
        ["adv"] = ["actually", "apparently", "approximately", "barely", "constantly", "directly", "effectively", "eventually", "frequently", "gradually", "hardly", "largely", "merely", "mainly", "nearly", "obviously", "precisely", "primarily", "rapidly", "rarely", "roughly", "slightly", "steadily", "virtually"]
    };

    [GeneratedRegex(@"^\s*(?:abbr|pref|suf|comb|contr)\.|^(?:\[[^\]]*\]\s*)?(?:[a-z]+\.\s*)?(?:\[\s*)?(?:古英语|古义|古语|古用法|方言|古(?:\s|\]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonstandardGlossPattern();

    [GeneratedRegex(@"[\u4E00-\u9FFF]", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseCharPattern();

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
        var answerPhonetic = FormatPhonetic(baseEntry?.phonetic);

        // Exclude base word, exact answer surface, and all direct inflections of baseWord from distractors.
        var excludedBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseWord, answerSurface };
        foreach (var form in exchangeMap.Values) excludedBases.Add(form);
        if (!string.IsNullOrWhiteSpace(item.MatchedForm)) excludedBases.Add(item.MatchedForm.Trim());

        var relations = await relatedWords.LookupAsync(baseWord, cancellationToken);
        var isPhraseTarget = baseWord.Contains(' ');

        var similarCandidates = new List<Candidate>();
        var derivativeCandidates = new List<Candidate>();
        var synonymCandidates = new List<Candidate>();

        if (!isPhraseTarget)
        {
            foreach (var rel in relations.Similar)
            {
                if (TryMakeCandidate(rel.Word, rel.Meaning, "形近词", excludedBases, answerMeaningCompact, dictionary, out var c))
                    similarCandidates.Add(c);
            }
            foreach (var rel in relations.Derivatives)
            {
                if (TryMakeCandidate(rel.Word, rel.Meaning, "派生词", excludedBases, answerMeaningCompact, dictionary, out var c))
                    derivativeCandidates.Add(c);
            }
            foreach (var rel in relations.Synonyms)
            {
                if (TryMakeCandidate(rel.Word, rel.Meaning, "近义词", excludedBases, answerMeaningCompact, dictionary, out var c))
                    synonymCandidates.Add(c);
            }
        }

        // Order synonyms to prefer ones with a distinct sense nuance instead of identical glosses,
        // and high-frequency words within each bucket.
        synonymCandidates = synonymCandidates
            .OrderBy(c => c.IdenticalGloss ? 1 : 0)
            .ThenBy(c => dictionary.FrequencyRank(c.BaseWord))
            .ToList();
        derivativeCandidates = derivativeCandidates
            .OrderBy(c => c.PrimaryPos != answerPos && c.PrimaryPos.Length > 0 ? 0 : 1)
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
                var display = FormatDistractorSurface(cand, inflectionTag, wasCapitalized, answerPos);
                if (!usedDisplays.Add(display)) continue;
                usedBases.Add(cand.BaseWord);
                chosen.Add(cand with { DisplayText = display });
                return true;
            }
            return false;
        }

        // Round 1: balanced trio — 1 形近词 + 1 派生词 + 1 近义词
        TryPickFrom(similarCandidates);
        TryPickFrom(derivativeCandidates);
        TryPickFrom(synonymCandidates);

        // Round 2: top up from any remaining candidates in related buckets
        for (var guard = 0; chosen.Count < 3 && guard < 6; guard++)
        {
            var before = chosen.Count;
            if (chosen.Count < 3) TryPickFrom(similarCandidates);
            if (chosen.Count < 3) TryPickFrom(derivativeCandidates);
            if (chosen.Count < 3) TryPickFrom(synonymCandidates);
            if (chosen.Count == before) break;
        }

        // Round 3: spelling-distance fallback from full dictionary
        if (chosen.Count < 3 && !isPhraseTarget)
        {
            foreach (var spelling in dictionary.SuggestSpellingCorrections(baseWord, 10))
            {
                if (chosen.Count >= 3) break;
                if (!TryMakeCandidate(spelling, null, "形近词", excludedBases, answerMeaningCompact, dictionary, out var c)) continue;
                if (usedBases.Contains(c.BaseWord)) continue;
                var display = FormatDistractorSurface(c, inflectionTag, wasCapitalized, answerPos);
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
                if (usedBases.Contains(c.BaseWord)) continue;
                var display = FormatDistractorSurface(c, inflectionTag, wasCapitalized, answerPos);
                if (!usedDisplays.Add(display)) continue;
                usedBases.Add(c.BaseWord);
                chosen.Add(c with { DisplayText = display });
            }
        }

        // Round 5: high-frequency academic fallback matching target POS (guaranteed 4 options)
        if (chosen.Count < 3)
        {
            var poolKey = NormalizePosBucket(answerPos);
            var fallbackOrder = new[] { poolKey, "v", "n", "adj", "adv" }.Distinct();
            foreach (var key in fallbackOrder)
            {
                if (chosen.Count >= 3) break;
                if (!FallbackPools.TryGetValue(key, out var words)) continue;
                foreach (var word in words.OrderBy(_ => Random.Shared.Next()))
                {
                    if (chosen.Count >= 3) break;
                    if (!TryMakeCandidate(word, null, "高频辨析", excludedBases, answerMeaningCompact, dictionary, out var c)) continue;
                    if (usedBases.Contains(c.BaseWord)) continue;
                    var display = FormatDistractorSurface(c, inflectionTag, wasCapitalized, answerPos);
                    if (!usedDisplays.Add(display)) continue;
                    usedBases.Add(c.BaseWord);
                    chosen.Add(c with { DisplayText = display });
                }
            }
        }

        // Build option records and shuffle A/B/C/D
        var rawOptions = new List<ContextQuizOption>(4);
        var answerNote = BuildCorrectAnalysis(answerSurface, baseWord, answerPos, answerMeaningCompact, inflectionTag);
        rawOptions.Add(new ContextQuizOption(
            "",
            answerSurface,
            baseWord,
            answerPhonetic,
            answerMeaningCompact.Length > 0 ? answerMeaningCompact : "目标词汇",
            "正确答案",
            answerNote,
            IsCorrect: true));

        foreach (var cand in chosen.Take(3))
        {
            rawOptions.Add(new ContextQuizOption(
                "",
                cand.DisplayText,
                cand.BaseWord,
                cand.Phonetic,
                cand.CompactMeaning,
                cand.Kind,
                BuildDistractorAnalysis(cand, baseWord, answerPos),
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

        var summary = BuildExplanationSummary(baseWord, answerSurface, answerMeaningCompact, inflectionTag, item.Translation);

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

    private sealed record Candidate(
        string BaseWord,
        string DisplayText,
        string Phonetic,
        string CompactMeaning,
        string PrimaryPos,
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
        var exchange = ParseExchange(entry?.exchange);
        var identicalGloss = targetCompactMeaning.Length > 0
            && string.Equals(StripPos(compact), StripPos(targetCompactMeaning), StringComparison.OrdinalIgnoreCase);

        candidate = new Candidate(
            entry?.word ?? word,
            entry?.word ?? word,
            FormatPhonetic(entry?.phonetic),
            compact,
            pos,
            kind,
            exchange,
            identicalGloss);
        return true;
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

        // Multi-word phrase handling
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

        // Build lookup map of form -> inflection tag for exact token matching
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

        // Pass 1: exact match against base word, matchedForm, or any known exchange inflection
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

        // Pass 2: stem prefix fallback (e.g. inflected form not listed in exchange)
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

    private static string FormatDistractorSurface(Candidate cand, string inflectionTag, bool capitalize, string targetPos)
    {
        var surface = cand.BaseWord;

        // Align inflections for non-derivative distractors (and plural nouns for derivative nouns),
        // so grammar clues don't trivially give away the answer.
        if (inflectionTag.Length > 0 && !cand.BaseWord.Contains(' '))
        {
            var shouldInflect = cand.Kind != "派生词"
                || (inflectionTag == "s" && cand.PrimaryPos.StartsWith("n", StringComparison.OrdinalIgnoreCase));
            if (shouldInflect)
            {
                if (cand.Exchange.TryGetValue(inflectionTag, out var inflected) && inflected.Length > 0)
                {
                    surface = inflected;
                }
                else if (inflectionTag == "p" && cand.Exchange.TryGetValue("d", out var part) && part.Length > 0)
                {
                    surface = part;
                }
                else if (inflectionTag == "d" && cand.Exchange.TryGetValue("p", out var past) && past.Length > 0)
                {
                    surface = past;
                }
            }
        }

        return ApplyCapitalization(surface, capitalize);
    }

    private static string BuildCorrectAnalysis(string answerSurface, string baseWord, string pos, string compactMeaning, string inflectionTag)
    {
        var sb = new StringBuilder();
        if (!answerSurface.Equals(baseWord, StringComparison.OrdinalIgnoreCase))
        {
            var tagLabel = InflectionLabel(inflectionTag);
            sb.Append(tagLabel.Length > 0 ? $"原形 {baseWord}（{tagLabel}），" : $"原形 {baseWord}，");
        }
        if (compactMeaning.Length > 0)
            sb.Append($"意为「{compactMeaning}」，契合句意与语境。");
        else
            sb.Append("符合本题语境与搭配。");
        return sb.ToString();
    }

    private static string BuildDistractorAnalysis(Candidate cand, string baseWord, string targetPos)
    {
        var formNote = !cand.DisplayText.Equals(cand.BaseWord, StringComparison.OrdinalIgnoreCase)
            ? $"（原形 {cand.BaseWord}）"
            : "";
        return cand.Kind switch
        {
            "形近词" => $"形近干扰{formNote}：拼写与 {baseWord} 接近，但意为「{cand.CompactMeaning}」，语义不符。",
            "派生词" when cand.PrimaryPos.Length > 0 && targetPos.Length > 0 && !cand.PrimaryPos.Equals(targetPos, StringComparison.OrdinalIgnoreCase)
                => $"派生词性干扰{formNote}：与 {baseWord} 同源，侧重 {cand.PrimaryPos}（{cand.CompactMeaning}），词性与此处语法位置不符。",
            "派生词" => $"同源派生干扰{formNote}：与 {baseWord} 同根，侧重「{cand.CompactMeaning}」，代入语境不贴切。",
            "近义词" => $"近义辨析{formNote}：虽与 {baseWord} 意思相近，但侧重「{cand.CompactMeaning}」，注意语境与固定搭配差异。",
            "已收藏词" => $"已收藏词干扰{formNote}：意为「{cand.CompactMeaning}」，不符合当前句意。",
            _ => $"语义干扰{formNote}：意为「{cand.CompactMeaning}」，代入句中语义不通。"
        };
    }

    private static string BuildExplanationSummary(string baseWord, string answerSurface, string compactMeaning, string inflectionTag, string translation)
    {
        var tagLabel = InflectionLabel(inflectionTag);
        var meaningPart = compactMeaning.Length > 0 ? $"（{compactMeaning}）" : "";
        var grammarPart = tagLabel.Length > 0 && !answerSurface.Equals(baseWord, StringComparison.OrdinalIgnoreCase)
            ? $"此处需用「{baseWord}」的{tagLabel}形式「{answerSurface}」。"
            : $"结合上下文，空格处应填「{answerSurface}」。";
        return $"本题考查「{baseWord}」{meaningPart}的语境辨析。{grammarPart}";
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

    private static string NormalizePosBucket(string pos)
    {
        var p = pos.ToLowerInvariant();
        if (p.StartsWith("vt") || p.StartsWith("vi") || p.StartsWith("v.")) return "v";
        if (p.StartsWith("adj") || p.StartsWith("a.")) return "adj";
        if (p.StartsWith("adv") || p.StartsWith("ad.")) return "adv";
        return "n";
    }

    private static string StripPos(string meaning)
    {
        var (pos, rest) = DictionaryEntry.SplitDisplaySense(meaning);
        return pos.Length > 0 ? rest.Trim() : meaning.Trim();
    }
}
