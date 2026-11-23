namespace MemoLex.Core;

internal sealed class RelatedPhrases(LocalDictionary dictionary)
{
    private readonly Lazy<Dictionary<string,int[]>> _index=new(()=>BuildIndex(dictionary));
    // Supplement the terse ECDICT glosses for these common idioms; keep the source index unchanged.
    private static readonly Dictionary<string,string> CommonMeanings=new(StringComparer.OrdinalIgnoreCase)
    {
        ["make sense"] = "有意义；有道理；讲得通",
        ["make sense of"] = "理解；弄懂",
        ["keep something in view"] = "把某事纳入考虑；把某事作为未来目标",
        ["have something up one's sleeve"] = "藏有后招；另有妙计",
        ["have something to do with"] = "与某人或某事有关",
        ["talk to"] = "和某人说话；责备某人",
        ["talk with"] = "和某人交谈；设法说服某人",
        ["talk out"] = "vt. 把事情说透；坦率地说；谈个明白",
        ["ask round"] = "请某人来家里坐坐；向多方打听"
    };
    internal static string CorrectMeaning(string phrase, string meaning) => CommonMeanings.GetValueOrDefault(phrase, meaning);

    private static readonly HashSet<string> Prepositions=new(StringComparer.OrdinalIgnoreCase)
        {"about","above","across","after","against","along","among","around","at","before","behind","below","beneath","beside","besides","between","beyond","by","despite","down","during","except","for","from","in","inside","into","near","of","off","on","onto","out","outside","over","past","since","through","throughout","till","to","toward","towards","under","underneath","until","up","upon","via","with","within","without"};
    private static readonly HashSet<string> CommonVerbHeads=new(StringComparer.OrdinalIgnoreCase)
        {"be","have","make","take","give","get","keep","pay","catch","lose","leave","set","put","break","come","go","do","find","hold","run","turn","show","tell","let","send","bring","draw","meet","fall","feel","stand","save","raise","play","read","write","speak","talk","wish","want","need","look","watch","hear","listen","think","bear","change","call","cut","carry"};
    private static readonly HashSet<string> CommonIdioms=new(StringComparer.OrdinalIgnoreCase)
        {"make sense","make sense of","make a difference","make use of","make sure","take place","take care of","take part in","take advantage of","take into account","take a look","take a break","pay attention to","get rid of","have a look","have to","give up","in spite of","as well as","at least","in fact","by the way","on the other hand","as a result","in addition","in addition to","as soon as","be able to","be aware of","look forward to","look up","come up with","come true","turn out","bring about","carry out","put up with","keep in mind","keep up with","out of control","out of date","lose sight of","catch sight of","catch up with","in terms of","in charge of","according to","because of","due to"};
    private static Dictionary<string,int[]> BuildIndex(LocalDictionary dictionary)
    {
        var postings=new Dictionary<string,List<int>>(StringComparer.OrdinalIgnoreCase);
        for(var id=0;id<dictionary.SourceRowCount;id++)
        {
            var phrase=dictionary.SourceWord(id);
            if(phrase.Length>100||!phrase.Contains(' ')||phrase.Any(c=>!char.IsAsciiLetterLower(c)&&c is not (' ' or '\'' or '’' or '-' or '.')))continue;
            var tokens=phrase.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(tokens.Length is <2 or >7)continue;
            foreach(var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(!postings.TryGetValue(token,out var ids))postings[token]=ids=[];
                ids.Add(id);
            }
        }
        return postings.ToDictionary(item=>item.Key,item=>item.Value.ToArray(),StringComparer.OrdinalIgnoreCase);
    }
    internal IReadOnlyList<RelatedWord> Lookup(string word,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        word=string.Join(' ',word.Trim().ToLowerInvariant().Split(' ',StringSplitOptions.RemoveEmptyEntries));
        if(word.Length==0||dictionary.Count==0)return [];
        var terms=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{word,0}};
        void AddForms(string source)
        {
            foreach(var field in (dictionary.Lookup(source)?.exchange??"").Split('/'))
            {
                var pair=field.Split(':',2);if(pair.Length!=2||pair[0]=="1")continue;
                foreach(var form in pair[1].Split(','))
                    if(form.Length>0&&form.All(c=>char.IsAsciiLetter(c)||c is '\'' or '-'))
                    {
                        var key=form.ToLowerInvariant();var priority=pair[0]=="0"?0:1;
                        if(!terms.TryGetValue(key,out var existing)||priority<existing)terms[key]=priority;
                    }
            }
        }
        AddForms(word);
        foreach(var form in terms.Keys.ToArray())if(!form.Equals(word,StringComparison.OrdinalIgnoreCase))AddForms(form);
        var index=_index.Value;cancellation.ThrowIfCancellationRequested();
        var ids=new HashSet<int>();
        foreach(var term in terms.Keys)
        {
            int[]? shortest=null;
            foreach(var token in term.Split(' ',StringSplitOptions.RemoveEmptyEntries))
            {
                if(!index.TryGetValue(token,out var found)){shortest=null;break;}
                if(shortest==null||found.Length<shortest.Length)shortest=found;
            }
            if(shortest!=null)ids.UnionWith(shortest);
        }
        var candidates=new List<(int Id,int Match,int Particle,int Start,int Tokens,int Length)>();
        foreach(var id in ids)
        {
            cancellation.ThrowIfCancellationRequested();
            var phrase=dictionary.SourceWord(id);
            if(phrase.Equals(word,StringComparison.OrdinalIgnoreCase))continue;
            var tokens=phrase.Split(' ',StringSplitOptions.RemoveEmptyEntries);var bestMatch=2;var bestStart=1;var particle=1;
            foreach(var (term,match) in terms)
            {
                var position=phrase.IndexOf(term,StringComparison.OrdinalIgnoreCase);
                while(position>=0)
                {
                    var end=position+term.Length;
                    if((position==0||phrase[position-1]==' ')&&(end==phrase.Length||phrase[end]==' '))
                    {
                        if(match<bestMatch){bestMatch=match;bestStart=position==0?0:1;particle=1;}
                        if(match==bestMatch)
                        {
                            bestStart=Math.Min(bestStart,position==0?0:1);
                            if(position==0&&tokens.Length==2&&Prepositions.Contains(tokens[1]))particle=0;
                        }
                        break;
                    }
                    position=phrase.IndexOf(term,position+1,StringComparison.OrdinalIgnoreCase);
                }
            }
            if(bestMatch<2)candidates.Add((id,bestMatch,particle,bestStart,tokens.Length,phrase.Length));
        }
        var described=new List<(RelatedWord Phrase,int Match,int Particle,int Technical,int Start,int Tokens,int Length)>();
        var predicates=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        bool Predicate(string head)
        {
            if(predicates.TryGetValue(head,out var known))return known;
            var entry=dictionary.Lookup(head);
            var valid=System.Text.RegularExpressions.Regex.IsMatch(entry?.translation??"",@"(?:^|\n)\s*(?:v(?:t|i)?|a(?:dj)?)\.",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(!valid)
            {
                var root=(entry?.exchange??"").Split('/').FirstOrDefault(field=>field.StartsWith("0:",StringComparison.Ordinal));
                if(root!=null)valid=System.Text.RegularExpressions.Regex.IsMatch(dictionary.Lookup(root[2..])?.translation??"",@"(?:^|\n)\s*(?:v(?:t|i)?|a(?:dj)?)\.",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return predicates[head]=valid;
        }
        foreach(var item in candidates.OrderBy(item=>item.Match).ThenBy(item=>CommonIdioms.Contains(dictionary.SourceWord(item.Id))?0:1).ThenBy(item=>item.Particle).ThenBy(item=>item.Start).ThenBy(item=>item.Tokens).ThenBy(item=>item.Length).ThenBy(item=>item.Id).Take(256))
        {
            cancellation.ThrowIfCancellationRequested();
            var entry=dictionary.LookupBySourceId(item.Id);var meaning=entry?.translation;
            if(string.IsNullOrWhiteSpace(meaning)||!meaning.Any(c=>c is >= '\u3400' and <= '\u9fff'))continue;
            meaning=CorrectMeaning(entry!.word,meaning);
            var tokens=entry!.word.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(!Prepositions.Contains(tokens[0]))
            {
                if(System.Text.RegularExpressions.Regex.IsMatch(meaning.TrimStart(),@"^(?:n|noun)\.",System.Text.RegularExpressions.RegexOptions.IgnoreCase))continue;
                var end=tokens.Length-1;
                while(end>0&&tokens[end].TrimEnd('.') is "sb" or "sth" or "somebody" or "someone" or "something" or "oneself" or "myself" or "yourself" or "himself" or "herself" or "itself" or "ourselves" or "themselves")end--;
                var hasPreposition=tokens.Any(Prepositions.Contains);
                if(!hasPreposition&&!CommonVerbHeads.Contains(tokens[0]))continue;
                if(tokens.Contains("of",StringComparer.OrdinalIgnoreCase)&&!Prepositions.Contains(tokens[end])&&!CommonVerbHeads.Contains(tokens[0]))continue;
                if(!Predicate(tokens[0]))continue;
                if(!Prepositions.Contains(tokens[end]))
                {
                    if(meaning.TrimStart().StartsWith('['))continue;
                }
            }
            described.Add((new RelatedWord(entry!.word,meaning),item.Match,item.Particle,meaning.TrimStart().StartsWith('[')?1:0,item.Start,item.Tokens,item.Length));
        }
        return described.OrderBy(item=>item.Match).ThenBy(item=>CommonIdioms.Contains(item.Phrase.Word)?0:1).ThenBy(item=>item.Particle).ThenBy(item=>item.Technical).ThenBy(item=>item.Start).ThenBy(item=>item.Tokens).ThenBy(item=>item.Length)
            .ThenBy(item=>item.Phrase.Word,StringComparer.OrdinalIgnoreCase).Take(24)
            .Select(item => item.Phrase with { Meaning = DictionaryEntry.FormatDisplayMeaning(item.Phrase.Meaning) }).ToArray();
    }
}
