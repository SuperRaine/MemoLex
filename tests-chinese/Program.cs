using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoLex.Core;

var root=Path.Combine(Path.GetTempPath(),"MemoLex.ChineseTests."+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed=0;
void Check(bool value,string message){if(!value)throw new Exception(message);}
async Task Test(string name,Func<Task> run){await run();passed++;Console.WriteLine("PASS "+name);}
var baseEntries=new[]{
    new DictionaryEntry{word="bank",translation="n. 银行",bnc="100"},
    new DictionaryEntry{word="localword",translation="旧库专用义"},
    new DictionaryEntry{word="sourcewins",translation="旧库含义",bnc="100000"},
    new DictionaryEntry{word="popularfallback",translation="精确的",bnc="1",collins="5",oxford="1"},
    new DictionaryEntry{word="generative",translation="a. 生殖的; 生产的",bnc="50000"},
    new DictionaryEntry{word="productive",translation="a. 能生产的",bnc="1",collins="5"},
    new DictionaryEntry{word="cheerful",translation="a. 舒畅的",bnc="100"},
    new DictionaryEntry{word="joyful",translation="a. 喜悦的",bnc="100"},
    new DictionaryEntry{word="fallbackbank",translation="n. 银行",bnc="500"},
    new DictionaryEntry{word="partialword",translation="a. 非常精确的",bnc="2"},
};
string FixtureDirectory()
{
    var directory=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
    using(var data=File.Create(Path.Combine(directory,"dictionary.data")))
    using(var index=new StreamWriter(Path.Combine(directory,"dictionary.index.tsv"),false,new UTF8Encoding(false)))
        foreach(var entry in baseEntries)
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(entry);index.WriteLine($"{entry.word}\t{data.Position}\t{bytes.Length}");data.Write(bytes);
        }
    var grams=new Dictionary<string,List<int>>();
    for(var id=0;id<baseEntries.Length;id++)
    {
        var text=string.Concat(baseEntries[id].translation.Where(c=>c is >= '\u3400' and <= '\u9fff'));
        var seen=new HashSet<string>();
        for(var i=0;i<text.Length;i++)
        {
            seen.Add("1:"+text[i]);if(i+1<text.Length)seen.Add("2:"+text.Substring(i,2));
        }
        foreach(var token in seen){if(!grams.TryGetValue(token,out var ids))grams[token]=ids=[];ids.Add(id);}
    }
    using(var data=File.Create(Path.Combine(directory,"chinese.postings")))
    using(var header=new BinaryWriter(data,Encoding.UTF8,true))
    using(var index=new StreamWriter(Path.Combine(directory,"chinese.tokens.tsv"),false,new UTF8Encoding(false)))
    {
        header.Write("CJX1"u8);header.Write(baseEntries.Length);
        foreach(var (token,ids) in grams)
        {
            var start=data.Position;var last=0;
            foreach(var id in ids)
            {
                var value=(uint)(id-last);last=id;
                while(value>=128){data.WriteByte((byte)((value&127)|128));value>>=7;}
                data.WriteByte((byte)value);
            }
            index.WriteLine($"{token}\t{start}\t{data.Position-start}");
        }
    }
    File.WriteAllText(Path.Combine(directory,"chinese.meta.json"),JsonSerializer.Serialize(new{
        dictionary_index_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"dictionary.index.tsv"))))}));
    File.WriteAllText(Path.Combine(directory,"chinese.synonyms.json"),JsonSerializer.Serialize(new Dictionary<string,object>{
        ["快乐"]=new{ids=new[]{Array.FindIndex(baseEntries,e=>e.word=="cheerful")},equivalents=Array.Empty<string>()}}));
    return directory;
}
LocalBriefDictionary LocalBriefFixture()
{
    var entries=new[]{("bank","n. 河岸"),("localword","adj. 精确的"),("exclusiveword","adj. 精确的"),
        ("sourcewins","adj. 精确的"),("generative","adj. 生殖的；生产的"),("productive","adj. 能生产的"),
        ("joyful","adj. 开心的"),("cheerful","adj. 欢快的"),("partialword","adj. 非常精确的")};
    using var section=new MemoryStream();using(var writer=new BinaryWriter(section,Encoding.UTF8,true))
    {
        for(var id=0;id<entries.Length;id++)
        {
            writer.Write(Encoding.UTF8.GetBytes(entries[id].Item1));writer.Write((byte)0);
            writer.Write((uint)100);writer.Write((uint)id);
            writer.Write(Encoding.UTF8.GetBytes(entries[id].Item2));writer.Write((byte)0);
        }
    }
    byte[] header=[0x4d,0x4c,0x58,1];var bytes=header.Concat(section.ToArray()).ToArray();
    var path=Path.Combine(root,Guid.NewGuid().ToString("N")+".bin");File.WriteAllBytes(path,bytes);
    return new(path,new(bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),Convert.ToHexString(header),header.Length,(int)section.Length,entries.Length));
}
try
{
    var directory=FixtureDirectory();using var dictionary=new LocalDictionary(directory,LocalBriefFixture());await dictionary.LoadAsync();
    using var search=new ChineseReverseSearch(dictionary,directory);await Task.WhenAll(search.LoadAsync(),search.LoadAsync());
    await Test("local dictionary-only senses and words are searchable even when absent from the old index",async()=>
    {
        var page=await search.SearchAsync("精确的",120);
        Check(page.Items.Any(r=>r.Word=="exclusiveword")&&page.Items.Any(r=>r.Word=="localword"),"local dictionary-only candidates were lost");
        foreach(var row in page.Items.Where(r=>r.Word is "localword" or "exclusiveword"))
            Check(row.IsPreferredSource&&row.IsExactMatch&&row.Meaning=="adj. 精确的"&&row.MatchedMeaning.Contains("精确的"),"The preferred meaning was not matched and displayed consistently");
        Check(dictionary.LookupBySourceId(0)?.translation=="n. 银行","Stable source IDs were changed");
    });
    await Test("An old meaning does not override a conflicting local dictionary meaning",async()=>
    {
        var banks=await search.SearchAsync("银行",120);var shore=await search.SearchAsync("河岸",120);
        Check(!banks.Items.Any(r=>r.Word=="bank")&&banks.Items.Any(r=>r.Word=="fallbackbank"),"The superseded ECDICT definition leaked into matching");
        Check(shore.Items.Any(r=>r.Word=="bank"&&r.IsPreferredSource&&r.Meaning=="n. 河岸"),"local dictionary's actual sense was missed");
    });
    await Test("local dictionary wins within an equal relevance tier and fallback remains available",async()=>
    {
        var rows=(await search.SearchAsync("精确的",120)).Items.ToList();
        var primary=rows.FindIndex(r=>r.Word=="sourcewins");var fallback=rows.FindIndex(r=>r.Word=="popularfallback");
        Check(primary>=0&&fallback>primary&&!rows[fallback].IsPreferredSource,"A popular fallback outranked an exact preferred-source match");
        Check(rows.FindIndex(r=>r.Word=="partialword")>fallback,"A preferred partial match outranked a complete fallback match");
        Check(rows.Select(r=>r.Word).Distinct(StringComparer.OrdinalIgnoreCase).Count()==rows.Count,"Sources produced duplicate cards");
    });
    await Test("Complete Chinese meanings still outrank partial adjective matches",async()=>
    {
        var rows=(await search.SearchAsync("生产的",120)).Items.ToList();
        Check(rows[0].Word=="generative"&&rows[0].IsExactMatch&&rows[0].IsPreferredSource&&rows[0].MatchedMeaning=="生产的","The complete production sense is not first");
        Check(rows.FindIndex(r=>r.Word=="productive")>0,"The useful partial match disappeared");
    });
    await Test("Chinese synonyms and WordNet results keep their local dictionary definitions",async()=>
    {
        var page=await search.SearchAsync("快乐",120);
        Check(page.Items.Any(r=>r.Word=="joyful"&&r.IsPreferredSource&&r.Meaning=="adj. 开心的"),"Chinese synonym expansion did not reach local dictionary's index");
        Check(page.Items.Any(r=>r.Word=="cheerful"&&r.IsPreferredSource&&r.Kind=="词网同义"&&r.Meaning=="adj. 欢快的"&&r.MatchedMeaning=="adj. 欢快的"),"WordNet fell back to an old or fabricated displayed meaning");
    });
    await Test("Missing local dictionary falls back to the bundled Chinese index",async()=>
    {
        using var fallbackDictionary=new LocalDictionary(directory,Path.Combine(root,"missing.bin"));await fallbackDictionary.LoadAsync();
        using var fallbackSearch=new ChineseReverseSearch(fallbackDictionary,directory);await fallbackSearch.LoadAsync();
        var rows=(await fallbackSearch.SearchAsync("银行")).Items;
        Check(rows.Any(r=>r.Word=="bank")&&rows.All(r=>!r.IsPreferredSource),"Missing local dictionary broke fallback search");
    });
    await Test("A valid local dictionary can search without optional fallback-index files",async()=>
    {
        var missing=FixtureDirectory();File.Delete(Path.Combine(missing,"chinese.meta.json"));
        using var primaryDictionary=new LocalDictionary(missing,LocalBriefFixture());await primaryDictionary.LoadAsync();
        using var primarySearch=new ChineseReverseSearch(primaryDictionary,missing);await primarySearch.LoadAsync();
        Check(primarySearch.Available&&(await primarySearch.SearchAsync("河岸")).Items.Any(r=>r.Word=="bank"&&r.IsPreferredSource),"Optional fallback files blocked the primary index");
    });
    await Test("Preferred-source searches honor cancellation and paging limits",async()=>
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try{await search.SearchAsync("精确",cancellationToken:cancellation.Token);throw new Exception("Canceled lookup completed");}catch(OperationCanceledException){}
        var first=await search.SearchAsync("精确的",1);var all=await search.SearchAsync("精确的",120);
        Check(first.Items.Count==1&&first.Total==all.Total&&first.Items[0]==all.Items[0],"Paging changed the ranking or total");
    });
    await Test("Damaged optional fallback metadata does not disable local dictionary search",async()=>
    {
        foreach(var corrupted in new[]{"chinese.meta.json","chinese.synonyms.json"})
        {
            var damaged=FixtureDirectory();File.WriteAllText(Path.Combine(damaged,corrupted),"{invalid json");
            using var primaryDictionary=new LocalDictionary(damaged,LocalBriefFixture());await primaryDictionary.LoadAsync();
            using var primarySearch=new ChineseReverseSearch(primaryDictionary,damaged);await primarySearch.LoadAsync();
            Check((await primarySearch.SearchAsync("河岸")).Items.Any(r=>r.Word=="bank"&&r.IsPreferredSource),"A damaged optional file blocked the primary index");
        }
    });
    if(args.Length==3&&args[0]=="--live")
    {
        await Test("Live local dictionary reverse searches use preferred meanings and preserve the source file",async()=>
        {
            var source=Path.GetFullPath(args[1]);var modified=File.GetLastWriteTimeUtc(source);
            string Digest(){using var input=File.OpenRead(source);return Convert.ToHexString(SHA256.HashData(input));}
            var before=Digest();var clock=Stopwatch.StartNew();
            using var local=new LocalDictionary(args[2],source);await local.LoadAsync();
            using var reverse=new ChineseReverseSearch(local,args[2]);await reverse.LoadAsync();
            var loadMs=clock.ElapsedMilliseconds;var reports=new List<object>();
            foreach(var query in new[]{"快乐","健谈","生产的","管理","安全","不快乐","目的地"})
            {
                clock.Restart();var page=await reverse.SearchAsync(query,120);
                foreach(var row in page.Items)
                {
                    var entry=local.Lookup(row.Word)!;
                    Check(row.Meaning==entry.translation&&row.IsPreferredSource==(entry.source=="local-brief"),"Live source/meaning mismatch");
                }
                if(query=="健谈")Check(page.Items.Any(r=>r.Word=="talkative"&&r.IsPreferredSource),"Talkative's live preferred sense was not found");
                if(query=="生产的")
                {
                    var generative=page.Items.FirstOrDefault(r=>r.Word=="generative");
                    Check(generative is {IsPreferredSource:true,IsExactMatch:false}&&generative.Meaning.Contains("能生产的"),"The live partial sense was lost or incorrectly labeled exact");
                }
                reports.Add(new{query,milliseconds=clock.ElapsedMilliseconds,total=page.Total,preferred=page.Items.Count(r=>r.IsPreferredSource),top=page.Items.Take(5).Select(r=>new{r.Word,r.SourceLabel,r.Kind}).ToArray()});
            }
            Check(Digest()==before&&File.GetLastWriteTimeUtc(source)==modified,"Live dictionary bytes or modification time changed");
            Console.WriteLine(JsonSerializer.Serialize(new{loadMilliseconds=loadMs,queries=reports}));
        });
    }
    else if(args.Length>0)throw new ArgumentException("Usage: [--live <combined.bin> <bundled-data-folder>]");
    Console.WriteLine($"{passed} Chinese dictionary-priority checks passed.");
}
finally
{
    var full=Path.GetFullPath(root);var parent=Path.GetFullPath(Path.GetTempPath());
    if(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar).Equals(parent.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)==true&&Path.GetFileName(full).StartsWith("MemoLex.ChineseTests.",StringComparison.Ordinal))Directory.Delete(full,true);
}
