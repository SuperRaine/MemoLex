namespace MemoLex.Core;

public sealed record ExtractedVocabularyItem(
    string Word,
    string Phonetic,
    string Meaning,
    IReadOnlyList<string> Tags,
    int FrequencyRank);

public static class VocabularyExtractor
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "across", "after", "again", "against", "almost", "alone", "along", "already", "also",
        "although", "always", "among", "another", "anyone", "anything", "around", "away", "back", "because",
        "become", "becomes", "been", "before", "began", "begin", "behind", "being", "below", "beside", "best",
        "better", "between", "both", "bring", "built", "call", "called", "came", "cannot", "care", "case",
        "change", "children", "city", "clear", "close", "come", "comes", "coming", "could", "country", "course",
        "days", "different", "does", "doing", "done", "down", "during", "each", "early", "easy", "either",
        "else", "enough", "even", "ever", "every", "everyone", "everything", "example", "face", "fact", "family",
        "feel", "felt", "find", "first", "five", "found", "four", "from", "full", "gave", "general", "getting",
        "give", "given", "gives", "going", "good", "great", "group", "hand", "hard", "have", "having", "head",
        "hear", "heard", "help", "here", "high", "himself", "herself", "itself", "hold", "home", "hope", "hour",
        "house", "however", "human", "idea", "important", "inside", "instead", "into", "just", "keep", "kept",
        "kind", "knew", "know", "known", "knows", "large", "last", "late", "later", "lead", "learn", "least",
        "leave", "left", "less", "life", "light", "like", "likely", "line", "little", "live", "living", "local",
        "long", "look", "looked", "looking", "looks", "made", "make", "makes", "making", "many", "matter",
        "maybe", "mean", "means", "meet", "might", "mind", "minute", "money", "month", "more", "morning", "most",
        "mother", "move", "much", "must", "myself", "name", "near", "need", "needs", "never", "next", "night",
        "nothing", "number", "often", "once", "only", "open", "order", "other", "others", "ourselves", "outside",
        "over", "part", "past", "people", "perhaps", "person", "place", "plan", "play", "point", "possible",
        "power", "present", "problem", "public", "quite", "rather", "read", "ready", "real", "really", "reason",
        "right", "room", "said", "same", "school", "second", "seem", "seemed", "seems", "seen", "self", "send",
        "sense", "service", "several", "shall", "short", "should", "show", "shown", "side", "simple", "since",
        "small", "social", "some", "someone", "something", "sometimes", "soon", "sorry", "sound", "speak",
        "start", "state", "stay", "still", "stop", "story", "strong", "student", "study", "such", "sure",
        "system", "take", "taken", "takes", "taking", "talk", "tell", "than", "thank", "that", "their", "them",
        "themselves", "then", "there", "these", "they", "thing", "things", "think", "thinking", "third", "this",
        "those", "though", "thought", "three", "through", "time", "times", "today", "together", "told", "took",
        "toward", "towards", "true", "turn", "turned", "twenty", "under", "understand", "until", "upon", "used",
        "using", "usually", "very", "view", "voice", "wait", "walk", "want", "wanted", "water", "week", "well",
        "went", "were", "what", "whatever", "when", "where", "whether", "which", "while", "white", "whole",
        "whom", "whose", "will", "with", "within", "without", "woman", "women", "word", "words", "work",
        "worked", "working", "works", "world", "would", "write", "written", "wrong", "year", "years", "young",
        "your", "yourself", "yourselves"
    };

    private static readonly Dictionary<string, (string Label, int Weight)> TagInfo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gre"] = ("GRE", 55),
        ["ielts"] = ("IELTS", 42),
        ["toefl"] = ("TOEFL", 42),
        ["ky"] = ("考研", 38),
        ["cet6"] = ("CET-6", 30),
        ["cet4"] = ("CET-4", 18),
        ["gk"] = ("高考", 10),
        ["zk"] = ("中考", 4)
    };

    public static IReadOnlyList<ExtractedVocabularyItem> Extract(string text, LocalDictionary dictionary, int limit = 24)
    {
        if (string.IsNullOrWhiteSpace(text) || limit <= 0) return Array.Empty<ExtractedVocabularyItem>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(ExtractedVocabularyItem Item, int Score, int Order)>();
        var order = 0;

        foreach (var token in ExampleText.Tokenize(text))
        {
            if (token.LookupWord is not { Length: >= 4 } rawWord) continue;
            if (rawWord.Length > 32) continue;

            var resolved = ExampleText.ResolveLookupWord(rawWord, w => dictionary.Lookup(w) != null);
            if (resolved.Length < 4 || StopWords.Contains(resolved)) continue;

            var entry = dictionary.Lookup(resolved);
            if (entry == null || string.IsNullOrWhiteSpace(entry.DisplayMeaning)) continue;

            // Prefer base lemma from exchange field (0:lemma) when available and meaningful.
            var lemma = ExtractLemma(entry.exchange);
            if (lemma is { Length: >= 4 } && !lemma.Equals(entry.word, StringComparison.OrdinalIgnoreCase))
            {
                if (StopWords.Contains(lemma)) continue;
                var baseEntry = dictionary.Lookup(lemma);
                if (baseEntry != null && !string.IsNullOrWhiteSpace(baseEntry.DisplayMeaning))
                    entry = baseEntry;
            }

            var canonical = entry.word.Trim();
            if (canonical.Length < 4 || StopWords.Contains(canonical) || !seen.Add(canonical)) continue;

            var rawTags = (entry.tag ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tagWeight = 0;
            var hasAdvancedTag = false;
            var labels = new List<string>(4);

            foreach (var tag in rawTags)
            {
                if (TagInfo.TryGetValue(tag, out var info))
                {
                    if (info.Weight > tagWeight) tagWeight = info.Weight;
                    if (info.Weight >= 30) hasAdvancedTag = true;
                    if (labels.Count < 3 && !labels.Contains(info.Label))
                        labels.Add(info.Label);
                }
            }

            var rank = dictionary.FrequencyRank(canonical);
            var effectiveRank = rank == int.MaxValue ? 0 : rank;

            // Filter out ultra-high-frequency basic words unless they carry an advanced exam tag.
            if (effectiveRank is > 0 and <= 750 && !hasAdvancedTag) continue;
            if (labels.Count == 0 && effectiveRank is > 0 and <= 1800) continue;

            var score = tagWeight;
            if (effectiveRank == 0) score += 22;
            else if (effectiveRank >= 5000) score += 32;
            else if (effectiveRank >= 2500) score += 24;
            else if (effectiveRank >= 1200) score += 14;
            else score += 5;

            if (canonical.Length >= 9) score += 8;
            else if (canonical.Length >= 7) score += 4;

            var meaning = CompactMeaning(entry.DisplayMeaning);
            var phonetic = string.IsNullOrWhiteSpace(entry.phonetic) ? "" : entry.phonetic.Trim('/');
            candidates.Add((new ExtractedVocabularyItem(canonical, phonetic, meaning, labels, effectiveRank), score, order++));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Order)
            .Take(limit)
            .Select(c => c.Item)
            .ToArray();
    }

    private static string? ExtractLemma(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange)) return null;
        foreach (var part in exchange.Split('/'))
        {
            if (part.StartsWith("0:", StringComparison.Ordinal))
            {
                var value = part[2..].Trim();
                if (value.Length >= 4 && !value.Contains(' '))
                    return value;
            }
        }
        return null;
    }

    private static string CompactMeaning(string displayMeaning)
    {
        var lines = displayMeaning.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join(" ", lines.Take(2));
        return joined.Length <= 72 ? joined : joined[..72] + "…";
    }
}
