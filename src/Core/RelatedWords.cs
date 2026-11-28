using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoLex.Core;

public readonly record struct RelatedSense(string PartOfSpeech, string Meaning);
public sealed record RelatedWord(string Word, string Meaning)
{
    public IEnumerable<RelatedSense> Senses => Meaning
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line =>
        {
            var (label, text) = DictionaryEntry.SplitDisplaySense(line);
            return new RelatedSense(label, text);
        });
}
public sealed record WordRelations(IReadOnlyList<RelatedWord> Synonyms, IReadOnlyList<RelatedWord> Similar, IReadOnlyList<RelatedWord> Derivatives,IReadOnlyList<RelatedWord> Phrases);
public sealed class RelatedWords(LocalDictionary dictionary, string? directory = null) : IDisposable
{
    private readonly string _directory = directory ?? Path.Combine(ApplicationPaths.RuntimeDirectory,"data");
    private readonly RelatedPhrases _phrases=new(dictionary);
    private ChineseReverseSearch? _chinese;
    private readonly Dictionary<string,(long Offset,int Length)> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Word,int Rank)> _spellings = [];
    private readonly object _gate=new();
    private FileStream? _data;
    private static readonly Regex NonstandardSpelling = new(
        @"^\s*(?:abbr|pref|suf|comb|contr)\.|^(?:\[[^\]]*\]\s*)?(?:[a-z]+\.\s*)?(?:\[\s*)?(?:古英语|古义|古语|古用法|方言|古(?:\s|\]))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnnotationPattern = new(@"\[[^\]]*\]|［[^］]*］|【[^】]*】|\([^)]*\)|（[^）]*）", RegexOptions.Compiled);
    private static readonly Regex InflectionGlossPattern = new(
        @"的(?:过去式|过去分词|现在分词|复数|第三人称单数|比较级|最高级|ing形式)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DomainJargonSegmentPattern = new(
        @"^(?:［[^］]{1,6}］|\[[^\]]{1,6}\]|【[^】]{1,6}】)",
        RegexOptions.Compiled);
    private static readonly Regex HanTokenPattern = new(@"[\u4E00-\u9FFF]{2,4}", RegexOptions.Compiled);
    private static readonly HashSet<string> WeakSingleCharSenses = new(StringComparer.Ordinal)
    {
        "的", "地", "得", "之", "为", "于", "在", "有", "一", "不", "人", "事", "物",
        "上", "下", "中", "出", "入", "来", "去", "成", "做", "作", "使", "让", "把",
        "被", "向", "从", "对", "与", "和", "或", "而", "及", "等", "以", "可", "能"
    };
    private static readonly Lazy<Dictionary<string, string>> DerivativeParts = new(LoadDerivativeParts);
    public bool Available => _data!=null;
    public void AttachChineseSearch(ChineseReverseSearch chinese) => _chinese = chinese;
    public IReadOnlyList<RelatedWord> LookupPhrases(string word, CancellationToken cancellationToken = default) => _phrases.Lookup(word, cancellationToken);
    private sealed class Record
    {
        public string[] synonyms {get;set;}=[];
        public string[] derivatives {get;set;}=[];
        public string gloss {get;set;}="";
    }
    public Task LoadAsync() => Task.Run(() =>
    {
        var index=Path.Combine(_directory,"related.index.tsv"); var data=Path.Combine(_directory,"related.data");
        if(!File.Exists(index)||!File.Exists(data)) return;
        foreach(var line in File.ReadLines(index))
        {
            var fields=line.Split('\t');
            if(fields.Length==3 && long.TryParse(fields[1],out var offset) && int.TryParse(fields[2],out var length)) _index.TryAdd(fields[0],(offset,length));
        }
        _data=File.Open(data,FileMode.Open,FileAccess.Read,FileShare.Read);
        var spellings=Path.Combine(_directory,"similar-words.tsv");
        if(File.Exists(spellings)) foreach(var line in File.ReadLines(spellings))
        {
            var fields=line.Split('\t'); if(fields.Length==2 && int.TryParse(fields[1],out var rank)) _spellings.Add((fields[0],rank));
        }
    });
    private Record? Read(string word)
    {
        if(_data==null||!_index.TryGetValue(word,out var item))return null;
        lock(_gate) { var bytes=new byte[item.Length];_data.Position=item.Offset;_data.ReadExactly(bytes);return JsonSerializer.Deserialize<Record>(bytes); }
    }
    private static Dictionary<string, string> LoadDerivativeParts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(RelatedWords).Assembly.GetManifestResourceStream("MemoLex.DerivativeParts.tsv");
        if (stream == null) return result;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t');
            if (tab > 0 && tab < line.Length - 1) result.TryAdd(line[..tab], line[(tab + 1)..]);
        }
        return result;
    }
    internal IReadOnlyList<string> SynonymSpellings(string word)
    {
        try{return Read(word)?.synonyms??[];}
        catch(Exception error) when(error is IOException or JsonException or ObjectDisposedException){return [];}
    }
    private static readonly Regex ProperNameGloss = new(
        @"(?:（|\()[^）)]{0,24}(?:男子名|女子名|人名|姓氏|地名|城市|港市|港口|首府|首都|州名|省名|郡名|男名|女名|澳大利亚|英格兰|苏格兰)[^）)]{0,16}(?:）|\))|^(?:\s*n\.\s*)?(?:男子名|女子名|人名|姓氏|地名)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<WordRelations> LookupAsync(string word,CancellationToken cancellationToken=default)
    {
        var (record, similar, derivatives, phrases) = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rec = Read(word);
            var baseEntry = dictionary.Lookup(word);
            var curatedSiblings = WordConfusionBank.GetSiblings(word);
            var curatedSet = new HashSet<string>(curatedSiblings, StringComparer.OrdinalIgnoreCase);

            var excludedInflections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { word };
            if (!string.IsNullOrWhiteSpace(baseEntry?.exchange))
            {
                foreach (var piece in baseEntry.exchange.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var colon = piece.IndexOf(':');
                    if (colon > 0 && colon < piece.Length - 1) excludedInflections.Add(piece[(colon + 1)..].Trim());
                }
            }
            if (rec?.derivatives != null)
            {
                foreach (var d in rec.derivatives)
                {
                    if (!curatedSet.Contains(d)) excludedInflections.Add(d);
                }
            }

            var nearby = new List<(string Word, int Tier, int Distance, int Rank)>();
            var normalized = word.ToLowerInvariant();
            if (word.Length is >= 3 and <= 24 && word.All(char.IsAsciiLetter))
            {
                var allowExtended = normalized.Length >= 5;
                var maxLenDiff = allowExtended ? 3 : 2;
                foreach (var item in _spellings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (excludedInflections.Contains(item.Word) || curatedSet.Contains(item.Word)) continue;
                    var cand = item.Word;
                    var lenDiff = Math.Abs(cand.Length - normalized.Length);
                    if (lenDiff > maxLenDiff) continue;

                    var sameFirst = cand[0] == normalized[0];
                    var sameLast = cand[^1] == normalized[^1];
                    var prefixLen = CommonPrefixLength(normalized, cand);
                    var suffixLen = CommonSuffixLength(normalized, cand);

                    var distance = SpellingDistance(normalized, cand, 2);
                    if (distance <= 2)
                    {
                        if (distance == 2 && normalized.Length <= 5 && item.Rank > 15000) continue;
                        var tier = distance == 1 ? 1 : (sameFirst || prefixLen >= 2 ? 2 : 3);
                        nearby.Add((cand, tier, distance, item.Rank));
                        continue;
                    }

                    if (!allowExtended || item.Rank > 24000) continue;

                    // Signal B: shared 3+ letter stem/prefix (e.g. statue ↔ station / static, kidnap ↔ kindle / kindred)
                    // or same initial + shared 3+ letter suffix
                    if (prefixLen >= 3 || (sameFirst && suffixLen >= 3 && lenDiff <= 2))
                    {
                        var d3 = SpellingDistance(normalized, cand, 3);
                        if (d3 <= 3)
                        {
                            var tier = prefixLen >= 4 ? 2 : 3;
                            nearby.Add((cand, tier, d3, item.Rank));
                            continue;
                        }
                    }

                    // Signal C: visual-frame confusability (same first 2 letters + same ending letter + strong ordered letter overlap,
                    // catching learner traps like kidnap ↔ kinship where edit distance is 4).
                    if (prefixLen >= 2 && sameLast && lenDiff <= 2 && item.Rank <= 20000)
                    {
                        var d4 = SpellingDistance(normalized, cand, 4);
                        if (d4 <= 4)
                        {
                            var lcs = LcsLength(normalized, cand);
                            var minLen = Math.Min(normalized.Length, cand.Length);
                            if (lcs * 10 >= minLen * 6)
                                nearby.Add((cand, 4, d4, item.Rank));
                        }
                    }
                }
            }

            var simList = new List<RelatedWord>(12);
            var addedSim = new HashSet<string>(excludedInflections, StringComparer.OrdinalIgnoreCase);

            void TryAddSimilar(string candidateWord, bool allowProper)
            {
                if (simList.Count >= 12 || !addedSim.Add(candidateWord)) return;
                var entry = dictionary.Lookup(candidateWord);
                if (entry == null || entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(entry.translation) ||
                    !entry.translation.Any(c => c is >= '\u3400' and <= '\u9fff')) return;
                if (!allowProper && LooksLikeProperName(entry)) return;
                if (!allowProper && IsPureInflection(entry, word)) return;
                var meaning = entry.DisplayMeaning;
                if (meaning.Length == 0) return;
                simList.Add(new RelatedWord(entry.word, meaning));
            }

            foreach (var sib in curatedSiblings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryAddSimilar(sib, allowProper: true);
            }

            foreach (var candidate in nearby
                .OrderBy(value => value.Tier)
                .ThenBy(value => value.Distance)
                .ThenBy(value => value.Rank)
                .ThenBy(value => value.Word, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryAddSimilar(candidate.Word, allowProper: false);
                if (simList.Count >= 12) break;
            }
            return (rec, (IReadOnlyList<RelatedWord>)simList, DescribeWords(word, rec?.derivatives ?? [], supplementPartOfSpeech: true, preferFrequency: true), _phrases.Lookup(word, cancellationToken));
        }, cancellationToken);

        var synonyms = await BuildSynonymsAsync(word, record?.synonyms ?? [], cancellationToken);
        return new WordRelations(synonyms, similar, derivatives, phrases);
    }

    private static bool LooksLikeProperName(DictionaryEntry entry)
    {
        if (entry.word.Length > 0 && char.IsUpper(entry.word[0])) return true;
        var lines = entry.DisplayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 && lines.All(line => ProperNameGloss.IsMatch(line));
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    private static int CommonSuffixLength(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[a.Length - 1 - i] == b[b.Length - 1 - i]) i++;
        return i;
    }

    private static int LcsLength(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
                curr[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], curr[j - 1]);
            (prev, curr) = (curr, prev);
            Array.Clear(curr);
        }
        return prev[b.Length];
    }

    private IReadOnlyList<RelatedWord> DescribeWords(string word, IEnumerable<string> words, bool supplementPartOfSpeech = false, bool preferFrequency = false)
    {
        var items = words.Where(value => value.Length is > 0 and <= 100 && char.IsAsciiLetter(value[0])
            && value.All(character => char.IsAsciiLetter(character) || character is ' ' or '.' or '’' or '\'' or '-')
            && !value.Equals(word, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => (Word: value, Entry: dictionary.Lookup(value)));

        var ordered = preferFrequency
            ? items
                .OrderBy(v => string.IsNullOrWhiteSpace(v.Entry?.translation) ? 1 : 0)
                .ThenBy(v => v.Entry != null && (v.Entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(v.Entry.translation)) ? 1 : 0)
                .ThenBy(v => v.Word.Contains(' ') ? 1 : 0)
                .ThenBy(v => dictionary.FrequencyRank(v.Word))
            : items.OrderBy(v => string.IsNullOrWhiteSpace(v.Entry?.translation) ? 1 : 0);

        return ordered.Take(12).Select(value =>
        {
            var meaning = value.Entry?.DisplayMeaning ?? "";
            if (meaning.Length == 0) meaning = DictionaryEntry.FormatDisplayMeaning(Read(value.Word)?.gloss ?? "暂无释义");
            if (supplementPartOfSpeech && meaning.Length > 0 && DerivativeParts.Value.TryGetValue(value.Word, out var partOfSpeech)
                && !meaning.Split('\n').Any(line => DictionaryEntry.SplitDisplaySense(line).PartOfSpeech.Length > 0))
                meaning = partOfSpeech + " " + meaning;
            return new RelatedWord(value.Word, meaning);
        }).ToArray();
    }

    private static bool TryGetInflectionLemma(DictionaryEntry entry, out string lemma)
    {
        lemma = "";
        if (string.IsNullOrWhiteSpace(entry.exchange)) return false;
        foreach (var piece in entry.exchange.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (piece.StartsWith("0:", StringComparison.Ordinal) && piece.Length > 2)
            {
                var candidate = piece[2..].Trim();
                if (candidate.Length >= 2 && candidate.All(char.IsAsciiLetter) && !candidate.Equals(entry.word, StringComparison.OrdinalIgnoreCase))
                {
                    lemma = candidate;
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsPureInflection(DictionaryEntry entry, string targetWord)
    {
        if (!TryGetInflectionLemma(entry, out var lemma)) return false;
        // Keep if the target itself is an inflection or if the word is an independent high-value lemma without inflection-marker gloss.
        if (InflectionGlossPattern.IsMatch(entry.translation)) return true;
        if (targetWord.StartsWith(lemma, StringComparison.OrdinalIgnoreCase) && lemma.Length >= 4) return true;
        return string.IsNullOrWhiteSpace(entry.tag) && entry.oxford != "1" && string.IsNullOrWhiteSpace(entry.collins);
    }

    private bool IsRecognizedLearnerWord(DictionaryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.tag) || entry.oxford == "1") return true;
        if (!string.IsNullOrWhiteSpace(entry.collins) && entry.collins != "0") return true;
        return dictionary.FrequencyRank(entry.word) <= 22000;
    }

    private static HashSet<string> ExtractPosGroups(string displayMeaning)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = DictionaryEntry.SplitDisplaySense(line).PartOfSpeech.Trim().ToLowerInvariant();
            if (p.StartsWith("prep")) set.Add("prep");
            else if (p.StartsWith("conj")) set.Add("conj");
            else if (p.StartsWith("vt") || p.StartsWith("vi") || p.StartsWith("v.")) set.Add("v");
            else if (p.StartsWith("adj") || p.StartsWith("a.")) set.Add("adj");
            else if (p.StartsWith("adv") || p.StartsWith("ad.")) set.Add("adv");
            else if (p.StartsWith("n.")) set.Add("n");
        }
        return set;
    }

    private static string ExtractPrimaryPosGroup(string displayMeaning)
    {
        var first = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var p = DictionaryEntry.SplitDisplaySense(first).PartOfSpeech.Trim().ToLowerInvariant();
        if (p.StartsWith("prep")) return "prep";
        if (p.StartsWith("conj")) return "conj";
        if (p.StartsWith("vt") || p.StartsWith("vi") || p.StartsWith("v.")) return "v";
        if (p.StartsWith("adj") || p.StartsWith("a.")) return "adj";
        if (p.StartsWith("adv") || p.StartsWith("ad.")) return "adv";
        if (p.StartsWith("n.")) return "n";
        return "";
    }

    private static HashSet<string> ExtractAlignmentSenses(string displayMeaning, int maxLines = int.MaxValue)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(maxLines))
        {
            var (_, rest) = DictionaryEntry.SplitDisplaySense(line);
            var body = rest.Length > 0 ? rest : line;
            foreach (var rawSeg in body.Split(['；', ';', '，', ',', '、', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (DomainJargonSegmentPattern.IsMatch(rawSeg)) continue;
                var cleaned = AnnotationPattern.Replace(rawSeg, "").Trim();
                if (cleaned.Length > 2 && (cleaned.EndsWith('的') || cleaned.EndsWith('地'))) cleaned = cleaned[..^1];
                if (cleaned.Length == 1 && cleaned[0] is >= '\u4E00' and <= '\u9FFF' && !WeakSingleCharSenses.Contains(cleaned))
                {
                    set.Add(cleaned);
                }
                else if (cleaned.Length is >= 2 and <= 4 && cleaned.All(c => c is >= '\u4E00' and <= '\u9FFF'))
                {
                    set.Add(cleaned);
                }
                else
                {
                    foreach (Match m in HanTokenPattern.Matches(cleaned))
                        set.Add(m.Value);
                }
            }
        }
        return set;
    }

    private static int SenseOverlapScore(HashSet<string> targetSenses, HashSet<string> candSenses)
    {
        if (targetSenses.Count == 0 || candSenses.Count == 0) return 0;
        var score = 0;
        foreach (var t in targetSenses)
        {
            if (candSenses.Contains(t))
            {
                score += t.Length >= 2 ? 3 : 2;
                continue;
            }
            if (t.Length == 1)
            {
                // Standalone concrete 1-char noun like "山" matches compound glosses like "山峰" / "小山" / "山脉".
                if (candSenses.Any(c => c.Length is >= 2 and <= 3 && c.Contains(t, StringComparison.Ordinal)))
                    score += 1;
                continue;
            }
            if (candSenses.Any(c => c.Length >= 2 && (t.Contains(c, StringComparison.Ordinal) || c.Contains(t, StringComparison.Ordinal))))
                score += 2;
        }
        return score;
    }

    private static bool SharesSameStem(string a, string b)
    {
        var la = a.ToLowerInvariant();
        var lb = b.ToLowerInvariant();
        if (la == lb) return true;
        var min = Math.Min(la.Length, lb.Length);
        if (min < 5) return false;
        var stem = la.Length <= lb.Length ? la : lb;
        var longer = la.Length <= lb.Length ? lb : la;
        if (stem.EndsWith('e')) stem = stem[..^1];
        return stem.Length >= 5 && longer.StartsWith(stem, StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<RelatedWord>> BuildSynonymsAsync(string word, IReadOnlyList<string> rawWordNet, CancellationToken cancellationToken)
    {
        var baseEntry = dictionary.Lookup(word);
        var baseDisplay = baseEntry?.DisplayMeaning ?? "";
        var targetPrimaryPos = ExtractPrimaryPosGroup(baseDisplay);
        var targetPosGroups = ExtractPosGroups(baseDisplay);
        var targetSenses = ExtractAlignmentSenses(baseDisplay);
        var isFunctionWord = targetPrimaryPos is "prep" or "conj";

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { word };
        if (!string.IsNullOrWhiteSpace(baseEntry?.exchange))
        {
            foreach (var part in baseEntry.exchange.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = part.IndexOf(':');
                if (colon > 0 && colon < part.Length - 1) excluded.Add(part[(colon + 1)..].Trim());
            }
        }

        // 1. Curated high-frequency usage/direction synonym siblings come first.
        var curatedSiblings = WordSynonymBank.GetSiblings(word);
        var curatedList = new List<RelatedWord>();
        var curatedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sib in curatedSiblings)
        {
            if (excluded.Contains(sib) || !curatedSet.Add(sib)) continue;
            var entry = dictionary.Lookup(sib);
            if (entry == null || string.IsNullOrWhiteSpace(entry.DisplayMeaning)) continue;
            curatedList.Add(new RelatedWord(entry.word, entry.DisplayMeaning));
        }

        // 2. Clean & semantically align WordNet candidates against the target word's displayed definition.
        var alignedWordNet = new List<(RelatedWord Item, int PosMatch, int Overlap, int Rank)>();
        var unalignedWordNet = new List<(RelatedWord Item, int PosMatch, int Rank)>();
        var seenWn = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        seenWn.UnionWith(curatedSet);

        foreach (var raw in rawWordNet)
        {
            var candidate = raw.Trim();
            if (candidate.Length is < 2 or > 40 || !char.IsAsciiLetter(candidate[0]) || char.IsUpper(candidate[0])
                || !candidate.All(ch => char.IsAsciiLetter(ch) || ch is ' ' or '-'))
                continue;

            var entry = dictionary.Lookup(candidate);
            if (entry == null || entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(entry.translation)) continue;
            if (LooksLikeProperName(entry)) continue;

            // Normalize inflected forms (e.g. hijacked -> hijack, abductions -> abduction) to their base lemma.
            if (TryGetInflectionLemma(entry, out var lemma))
            {
                if (excluded.Contains(lemma) || curatedSet.Contains(lemma)) continue;
                var lemmaEntry = dictionary.Lookup(lemma);
                if (lemmaEntry != null && !lemmaEntry.HasHistoricalDefinition && !NonstandardSpelling.IsMatch(lemmaEntry.translation) && !LooksLikeProperName(lemmaEntry))
                    entry = lemmaEntry;
                else if (InflectionGlossPattern.IsMatch(entry.translation))
                    continue;
            }

            if (char.IsUpper(entry.word[0]) || !seenWn.Add(entry.word)) continue;
            if (entry.word.Contains(' ')) continue;
            if (!IsRecognizedLearnerWord(entry)) continue;

            var display = entry.DisplayMeaning;
            if (string.IsNullOrWhiteSpace(display)) continue;

            var candPosGroups = ExtractPosGroups(display);
            var candPrimaryPos = ExtractPrimaryPosGroup(display);
            var posMatch = targetPrimaryPos.Length > 0 && candPrimaryPos == targetPrimaryPos ? 2
                : (targetPosGroups.Count > 0 && candPosGroups.Overlaps(targetPosGroups) ? 1 : 0);

            var primaryOverlap = SenseOverlapScore(targetSenses, ExtractAlignmentSenses(display, maxLines: 1));
            var overlap = SenseOverlapScore(targetSenses, ExtractAlignmentSenses(display));
            var rank = dictionary.FrequencyRank(entry.word);
            // Reject candidates whose primary POS differs and only match via a single buried secondary sense (e.g. spirit "vt. 诱拐" for kidnap).
            var semanticallyAligned = targetSenses.Count == 0
                || (posMatch == 2 && overlap > 0)
                || (posMatch == 1 && (primaryOverlap > 0 || overlap >= 5))
                || (isFunctionWord && overlap > 0);
            if (semanticallyAligned)
                alignedWordNet.Add((new RelatedWord(entry.word, display), posMatch, overlap, rank));
            else if (posMatch >= 1 || isFunctionWord)
                unalignedWordNet.Add((new RelatedWord(entry.word, display), posMatch, rank));
        }

        var orderedAlignedWn = alignedWordNet
            .OrderByDescending(x => x.PosMatch)
            .ThenByDescending(x => x.Overlap)
            .ThenBy(x => x.Rank)
            .Select(x => x.Item)
            .ToList();

        // 3. Supplement via Chinese semantic overlap whenever curated + aligned WordNet has fewer than 8 items.
        var rankedSup = new List<RelatedWord>();
        if (_chinese != null && _chinese.Available && !word.Contains(' ') && !string.IsNullOrWhiteSpace(baseDisplay)
            && (isFunctionWord || curatedList.Count + orderedAlignedWn.Count < 8))
        {
            var coreGlosses = ExtractCoreChineseGlosses(baseDisplay);
            if (coreGlosses.Count > 0)
            {
                var stem = word.ToLowerInvariant();
                if (stem.Length > 4 && stem.EndsWith('e')) stem = stem[..^1];
                var supplemental = new List<(string Word, string Meaning, int PosMatch, int Overlap, int GlossIndex, double Score, int Rank)>();
                var seenSup = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
                seenSup.UnionWith(curatedSet);
                foreach (var w in orderedAlignedWn) seenSup.Add(w.Word);

                for (var gi = 0; gi < Math.Min(3, coreGlosses.Count); gi++)
                {
                    var gloss = coreGlosses[gi];
                    cancellationToken.ThrowIfCancellationRequested();
                    ChineseSearchPage page;
                    try { page = await _chinese.SearchAsync(gloss, 24, cancellationToken); }
                    catch (Exception ex) when (ex is ArgumentException or IOException) { continue; }

                    foreach (var hit in page.Items)
                    {
                        var w = hit.Word.Trim();
                        if (w.Length is < 2 or > 26 || w.Contains(' ') || w.Contains('-') || !w.All(char.IsAsciiLetter)) continue;
                        if (char.IsUpper(w[0])) continue;
                        var entry = dictionary.Lookup(w);
                        if (entry == null || entry.HasHistoricalDefinition || NonstandardSpelling.IsMatch(entry.translation)) continue;
                        if (LooksLikeProperName(entry)) continue;

                        if (TryGetInflectionLemma(entry, out var lemma))
                        {
                            if (excluded.Contains(lemma) || curatedSet.Contains(lemma)) continue;
                            var lemmaEntry = dictionary.Lookup(lemma);
                            if (lemmaEntry != null && !lemmaEntry.HasHistoricalDefinition && !NonstandardSpelling.IsMatch(lemmaEntry.translation)
                                && !LooksLikeProperName(lemmaEntry) && !char.IsUpper(lemmaEntry.word[0]))
                                entry = lemmaEntry;
                            else
                                continue;
                        }

                        var candWord = entry.word;
                        if (char.IsUpper(candWord[0]) || SharesSameStem(candWord, word)) continue;
                        if (!seenSup.Add(candWord)) continue;
                        if (!IsRecognizedLearnerWord(entry)) continue;

                        var display = entry.DisplayMeaning;
                        if (string.IsNullOrWhiteSpace(display)) continue;

                        var primaryOverlap = SenseOverlapScore(targetSenses, ExtractAlignmentSenses(display, maxLines: 1));
                        var overlap = SenseOverlapScore(targetSenses, ExtractAlignmentSenses(display));
                        if (overlap == 0) continue;

                        var candPosGroups = ExtractPosGroups(display);
                        var candPrimaryPos = ExtractPrimaryPosGroup(display);
                        var posMatch = targetPrimaryPos.Length > 0 && candPrimaryPos == targetPrimaryPos ? 2
                            : (targetPosGroups.Count > 0 && candPosGroups.Overlaps(targetPosGroups) ? 1 : 0);
                        if (!isFunctionWord && posMatch == 0) continue;
                        if (!isFunctionWord && posMatch == 1 && primaryOverlap == 0 && overlap < 5) continue;

                        var rank = dictionary.FrequencyRank(candWord);
                        supplemental.Add((candWord, display, posMatch, overlap, gi, hit.Score, rank));
                    }
                }

                rankedSup = supplemental
                    .OrderByDescending(x => x.PosMatch)
                    .ThenBy(x => x.GlossIndex)
                    .ThenByDescending(x => x.Overlap)
                    .ThenBy(x => x.Rank)
                    .ThenByDescending(x => x.Score)
                    .Select(x => new RelatedWord(x.Word, x.Meaning))
                    .ToList();
            }
        }

        // 4. Merge in priority order and avoid cross-POS same-stem duplicates (e.g. abduct + abduction).
        var merged = new List<RelatedWord>(12);
        var added = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);

        void TryAppend(RelatedWord item, bool allowStemSibling)
        {
            if (merged.Count >= 12 || !added.Add(item.Word)) return;
            if (!allowStemSibling && merged.Any(existing => SharesSameStem(existing.Word, item.Word))) return;
            merged.Add(item);
        }

        foreach (var item in curatedList) TryAppend(item, allowStemSibling: true);

        var primary = isFunctionWord ? (IEnumerable<RelatedWord>)rankedSup : orderedAlignedWn;
        var secondary = isFunctionWord ? (IEnumerable<RelatedWord>)orderedAlignedWn : rankedSup;

        foreach (var item in primary) TryAppend(item, allowStemSibling: false);
        foreach (var item in secondary) TryAppend(item, allowStemSibling: false);

        // Only fall back to unaligned WordNet entries if the word has fewer than 2 semantically aligned synonyms.
        if (merged.Count < 2)
        {
            foreach (var fallback in unalignedWordNet
                .OrderByDescending(x => x.PosMatch)
                .ThenBy(x => x.Rank)
                .Take(3))
            {
                TryAppend(fallback.Item, allowStemSibling: false);
                if (merged.Count >= 3) break;
            }
        }

        return merged;
    }

    private static List<string> ExtractCoreChineseGlosses(string displayMeaning)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = displayMeaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines.Take(2))
        {
            var (_, rest) = DictionaryEntry.SplitDisplaySense(line);
            var body = rest.Length > 0 ? rest : line;
            foreach (var rawSeg in body.Split(['，', ',', '；', ';', '、', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (DomainJargonSegmentPattern.IsMatch(rawSeg)) continue;
                var cleaned = AnnotationPattern.Replace(rawSeg, "");
                var token = cleaned.Trim();
                if (token.Length > 2 && (token.EndsWith('的') || token.EndsWith('地'))) token = token[..^1];
                if (token.Length is >= 2 and <= 4 && token.All(c => c is >= '\u4E00' and <= '\u9FFF'))
                {
                    if (seen.Add(token)) results.Add(token);
                }
                else
                {
                    var m = HanTokenPattern.Match(token);
                    if (m.Success && seen.Add(m.Value)) results.Add(m.Value);
                }
                if (results.Count >= 4) return results;
            }
        }
        return results;
    }
    // Optimal string alignment distance treats an adjacent transposition as one edit.
    internal static int SpellingDistance(string a,string b,int maximum)
    {
        if(Math.Abs(a.Length-b.Length)>maximum)return maximum+1;
        var previous=Enumerable.Range(0,b.Length+1).ToArray();var older=(int[])previous.Clone();var current=new int[b.Length+1];
        for(var i=1;i<=a.Length;i++)
        {
            Array.Fill(current,maximum+1);current[0]=i;var best=maximum+1;
            for(var j=Math.Max(1,i-maximum);j<=Math.Min(b.Length,i+maximum);j++)
            {
                current[j]=Math.Min(previous[j]+1,Math.Min(current[j-1]+1,previous[j-1]+(a[i-1]==b[j-1]?0:1)));
                if(i>1&&j>1&&a[i-1]==b[j-2]&&a[i-2]==b[j-1])current[j]=Math.Min(current[j],older[j-2]+1);
                best=Math.Min(best,current[j]);
            }
            if(best>maximum)return maximum+1;
            (older,previous,current)=(previous,current,older);
        }
        return previous[b.Length];
    }
    public void Dispose()=>_data?.Dispose();
}
