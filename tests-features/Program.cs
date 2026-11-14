using System.Net;
using System.Text;
using System.Text.Json;
using MemoLex.Core;
using MemoLex.Services;

var passed=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);}
async Task Test(string name,Func<Task> action){await action();passed++;Console.WriteLine("PASS "+name);}
await Test("A parked pointer never starts OCR when video frames change and a stop is consumed only once",()=>
{
    var gate=new HoverMotionGate();var time=DateTimeOffset.UtcNow;var parked=new ScreenPoint(100,100);
    gate.Observe(parked,time);
    for(var frame=1;frame<=120;frame++){gate.Observe(parked,time.AddSeconds(frame));Check(!gate.TryBegin(time.AddSeconds(frame),500),"Stationary video rearmed recognition");}
    var moved=new ScreenPoint(120,100);gate.Observe(moved,time.AddSeconds(121));
    Check(!gate.TryBegin(time.AddSeconds(121).AddMilliseconds(499),500)&&gate.TryBegin(time.AddSeconds(121).AddMilliseconds(500),500),"Dwell timing failed");
    for(var frame=122;frame<=240;frame++){gate.Observe(moved,time.AddSeconds(frame));Check(!gate.TryBegin(time.AddSeconds(frame),500),"A failed or completed stop started another OCR attempt");}
    return Task.CompletedTask;
});
await Test("Hover jitter, cancellation and app activation require another intentional movement or modifier",()=>
{
    var gate=new HoverMotionGate();var now=DateTimeOffset.UtcNow;gate.Observe(new(20,20),now);gate.Observe(new(22,22),now.AddSeconds(1));
    Check(!gate.TryBegin(now.AddSeconds(2),500),"Small pointer jitter rearmed lookup");
    gate.Arm(new(22,22),now.AddSeconds(3));Check(gate.TryBegin(now.AddSeconds(4),500),"An explicit modifier could not start lookup");
    gate.Suppress(new(22,22));Check(!gate.TryBegin(now.AddHours(1),500),"Dismissal retriggered lookup");
    gate.Reset();gate.Observe(new(100,100),now.AddHours(2));Check(!gate.TryBegin(now.AddHours(3),500),"Re-enabling the app triggered a parked pointer");return Task.CompletedTask;
});
await Test("Frame stability accepts a static word and rejects changing picture content or locations",()=>
{
    var samples=Enumerable.Repeat((byte)180,288).ToArray();var first=new HoverSurface(new(20,20,100,30),samples);
    Check(HoverSurfaces.Same(first,new(first.Bounds,(byte[])samples.Clone())),"Identical word was rejected");
    Check(HoverSurfaces.Same(first,new(first.Bounds,samples.Select(x=>(byte)(x+2)).ToArray())),"Small rendering noise was rejected");
    var changed=(byte[])samples.Clone();Array.Fill(changed,(byte)20,0,70);
    Check(!HoverSurfaces.Same(first,new(first.Bounds,changed))&&!HoverSurfaces.Same(first,new(new(30,20,100,30),samples)),"Changing video or scroll position was treated as stable");return Task.CompletedTask;
});
await Test("Image words require reliable English letters rather than short or uncertain Chinese-like marks",()=>
{
    Check(HoverWords.IsReliableImageWord("insight",96,new(0,0,130,30)),"A clear English word was rejected");
    Check(HoverWords.IsReliableImageWord("to",99,new(0,0,20,16)),"A high-confidence common short word was rejected");
    foreach(var word in new[]{"I","a","la","III","llll","中文"})Check(!HoverWords.IsReliableImageWord(word,98,new(0,0,30,24)),"Short or non-English marks were accepted");
    Check(!HoverWords.IsReliableImageWord("hello",60,new(0,0,70,24))&&HoverWords.Normalize("中文")==null,"Uncertain or Chinese text reached English lookup");return Task.CompletedTask;
});
await Test("Image hit testing accepts a nearby baseline but refuses gaps and ambiguous neighbours",()=>
{
    var word=new HoverWord("insight",new(10,10,46,12),"图片识别",96);
    Check(HoverWords.AtPointer([word],new(28,24)) is {Word:"insight"} hit&&hit.Bounds.Contains(new(28,24)),"A pointer just below the ink missed its word");
    Check(HoverWords.AtPointer([word],new(28,32))==null&&HoverWords.AtPointer([word],new(64,15))==null,"An empty area picked distant text");
    var neighbour=new HoverWord("reader",new(59,10,44,12),"图片识别",96);
    Check(HoverWords.AtPointer([word,neighbour],new(57.5,16))==null,"A gap between adjacent words picked an arbitrary neighbour");
    return Task.CompletedTask;
});
await Test("Mixed accessible text keeps English word spans separate from Chinese and punctuation",()=>
{
    var text="中文insight，Q: talkative（健谈的）";var spans=HoverWords.AccessibleSpans(text);
    Check(spans.Any(span=>span.Word=="insight"&&span.Start==2)&&spans.Any(span=>span.Word=="talkative")&&spans.Count(span=>span.IsChinese)==2,"Inline Chinese suppressed nearby English word spans");
    foreach(var span in spans)Check(text.Substring(span.Start,span.Length).Length==span.Length&&(span.IsChinese?span.Word==null:HoverWords.Normalize(text.Substring(span.Start,span.Length))==span.Word),"Accessible substring offsets were not preserved");
    return Task.CompletedTask;
});
await Test("Clipboard routes words, dictionary phrases, Chinese queries and paragraphs by language",()=>
{
    ClipboardLookupRequest Route(string text)=>ClipboardLookup.Classify(text,word=>word=="ice cream");
    Check(Route("talkative").Kind==ClipboardLookupKind.Word&&Route("ice cream").Kind==ClipboardLookupKind.Word,"A word or known phrase was translated as a paragraph");
    Check(Route("快乐").Kind==ClipboardLookupKind.Chinese,"Short Chinese did not use reverse search");
    foreach(var text in new[]{"A reader enjoys learning.","hello world","hello\nworld"})Check(Route(text) is {Kind:ClipboardLookupKind.Paragraph,Direction:"en|zh-CN"},"English paragraph misrouted");
    Check(Route("这是一段中文，需要翻译成英文。").Direction=="zh-CN|en","Chinese direction incorrect");
    foreach(var text in new[]{"",new string('a',5001),new string('b',64),"https://example.com/path","12345"})Check(Route(text).Kind==ClipboardLookupKind.Ignore,"Unsupported clipboard data accepted");
    return Task.CompletedTask;
});
await Test("Double copy requires two genuine C presses within 750ms in one foreground window",()=>
{
    var detector=new DoubleCopyDetector();var hwnd=(IntPtr)123;
    DoubleCopySignal? Key(bool down,long time,bool control=true,bool injected=false,IntPtr? foreground=null,int code=0x43)=>detector.Key(code,down,control,false,injected,time,7,foreground??hwnd);
    Check(Key(true,0)==null&&Key(true,20)==null,"Held C auto-repeat triggered");Key(false,40);
    Check(Key(true,500) is {InitialSequence:7,Foreground:var window}&&window==hwnd,"Second copy did not trigger");Key(false,510);
    Check(Key(true,600)==null,"Triple copy retriggered");Key(false,620);detector.Reset();
    Key(true,1000);Key(false,1010);Check(Key(true,1800)==null,"Expired copy triggered");Key(false,1810);
    Check(Key(true,1900,foreground:(IntPtr)456)==null,"Cross-window copy triggered");Key(false,1910);detector.Reset();
    Key(true,2000,injected:true);Key(false,2010,injected:true);Check(Key(true,2100)==null,"Injected first copy counted");Key(false,2110);detector.Reset();
    Key(true,2200,control:false);Key(false,2210);Check(Key(true,2300)==null,"Plain C counted as copy");Key(false,2310);
    Key(true,2320,code:0x56);Check(Key(true,2400)==null,"Another command did not reset copy pair");
    return Task.CompletedTask;
});
await Test("Notepad display preserves chapters and shows dictionary translations",()=>
{
    var content="# My words\r\ntalkative\r\nunknown\r\n";
    var rows=NotepadContent.Describe(content,word=>word=="talkative"?"adj. 健谈的\n爱讲话的":null);
    Check(rows.Count==3&&rows[0].IsChapter&&rows[1].Meaning=="adj. 健谈的 爱讲话的"&&rows[2].Meaning.Contains("暂无中文释义"),"Translated list lost words or chapter");
    Check(NotepadContent.Words(content).SequenceEqual(new[]{"talkative","unknown"}),"Heading counted as a word");return Task.CompletedTask;
});
await Test("Batch word edits preserve chapters and phrases, deduplicate and validate additions",()=>
{
    var original="# One\r\napple\r\n# Two\r\nbanana\r\napple pie\r\nAPPLE\r\n";
    var removed=NotepadContent.RemoveWords(original,new[]{"apple","BANANA"});
    Check(removed=="# One\r\n# Two\r\napple pie\r\n","Batch removal changed chapters, phrases or line endings");
    var added=NotepadContent.AddWords(removed,NotepadContent.ParseWords("pear,PEAR\r\nice cream；apple pie"));
    Check(added==removed+"pear\r\nice cream","Batch addition lost phrase boundaries, duplicates or line endings");
    try{NotepadContent.ParseWords("reader\n# injected heading");throw new Exception("Invalid import accepted");}catch(ArgumentException){}
    return Task.CompletedTask;
});
await Test("Unified content editing accepts batch paste without splitting headings or phrases",()=>
{
    var content="# One, two\r\ntalkative\r\nreader, learning；READER\r\n# Two\r\nice cream\r\n";
    Check(NotepadContent.NormalizeContent(content)=="# One, two\r\ntalkative\r\nreader\r\nlearning\r\n# Two\r\nice cream\r\n","Unified editing lost a heading, phrase, newline or duplicate check");
    Check(NotepadContent.NormalizeContent("\n")=="\n","Empty editing must remain empty for final-word confirmation");
    return Task.CompletedTask;
});
await Test("Local mnemonics survive restart and cloud content replaces local edits",async()=>
{
    var folder=Path.Combine(Path.GetTempPath(),"MemoLex.Mnemonics."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    try
    {
        var seed=Path.Combine(folder,"seed.json");var profile=Path.Combine(folder,"user.json");
        File.WriteAllText(seed,JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"talkative",[new("seed","词根词缀","talk + -ative",Origin:"builtin")]}}));
        var store=new MnemonicStore(profile,seed);await store.LoadAsync();Check(store.Get("TALKATIVE").Single().Text=="talk + -ative","Bundled fallback unavailable");
        store.Save("talkative",[new("edited","联想","一位健谈的读者")]);
        var reopened=new MnemonicStore(profile,seed);await reopened.LoadAsync();Check(reopened.Get("talkative").Single().Text=="一位健谈的读者","Local edit did not persist");
        reopened.Save("talkative",[new("cloud:1","词根","云端修订","1","cloud")]);
        var cached=new MnemonicStore(profile,seed);await cached.LoadAsync();Check(cached.Get("talkative").Single() is {Text:"云端修订",Origin:"cloud"},"Cloud override was not cached");
    }
    finally{Directory.Delete(folder,true);}
});
await Test("An unreadable personal mnemonic file remains intact instead of being overwritten",async()=>
{
    var folder=Path.Combine(Path.GetTempPath(),"MemoLex.Mnemonics."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    try
    {
        var profile=Path.Combine(folder,"user.json");File.WriteAllText(profile,"{broken");var store=new MnemonicStore(profile,Path.Combine(folder,"missing.json"));await store.LoadAsync();
        Check(store.LoadWarning!=null,"Corrupt file was silently ignored");
        try{store.Save("reader",[new("1","联想","读者")]);throw new Exception("Corrupt profile overwritten");}catch(IOException){}
        Check(File.ReadAllText(profile)=="{broken","Original personal content was lost");
    }
    finally{Directory.Delete(folder,true);}
});
await Test("A fresh profile includes the expanded mnemonic dataset and new memory bridges",async()=>
{
    var data=Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??"outputs/MemoLex/data";
    var store=new MnemonicStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".missing"),Path.Combine(data,"mnemonics.json"));await store.LoadAsync();
    Check(store.BundledCount>=10613&&store.Get("disagree").Any(note=>note.Text.Contains("agree")),"Bundled offline notes are missing");
    Check(store.Get("taste").Any(note=>note.Text.Contains("test"))&&store.Get("bitter").Any(note=>note.Text.Contains("逼他")),"New memory bridges are missing from a fresh profile");
});
await Test("Cloud mnemonic caches stay account-scoped and personal edits survive another account refresh",async()=>
{
    var root=Path.Combine(Path.GetTempPath(),"MemoLex.AccountNotes."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    try
    {
        var store=new MnemonicStore(Path.Combine(root,"personal.json"),Path.Combine(root,"missing.json"));await store.LoadAsync();
        store.Save("insight",[new("local","拆词联想","本地个人助记")]);
        store.SaveCloud("insight",[new("cloud:same","词根词缀","账号甲助记","same","cloud")],"account-a");
        store.SaveCloud("insight",[new("cloud:same","词根词缀","账号乙助记","same","cloud")],"account-b");
        Check(store.Get("insight","account-a").Single().Text=="账号甲助记"&&store.Get("insight","account-b").Single().Text=="账号乙助记","Cloud notes crossed accounts");
        Check(store.Get("insight",null).Single().Text=="本地个人助记","Logged-out fallback exposed cached cloud content");
        store.SaveEdit("insight",new("cloud:same","拆词联想","甲账号编辑后存到本地",Origin:"local"),"account-a");
        Check(store.Get("insight","account-b").Single().Text=="账号乙助记","Local editing erased another account cache");
        store.SaveCloud("insight",[],"account-b");
        Check(store.Get("insight","account-b").Any(n=>n.Text=="本地个人助记")&&store.Get("insight","account-b").Any(n=>n.Text.Contains("编辑后")),"Empty cloud refresh erased personal notes");
    }
    finally{if(Path.GetFileName(root).StartsWith("MemoLex.AccountNotes."))Directory.Delete(root,true);}
});
await Test("UTF-8 segmentation preserves all text, paragraphs and surrogate pairs",()=>
{
    var text=string.Concat(Enumerable.Repeat("A reader enjoys learning. 一位读者喜欢学习。😀\n\n",40));
    var chunks=TranslationClient.SplitSegments(text);
    Check(string.Concat(chunks)==text&&chunks.All(chunk=>Encoding.UTF8.GetByteCount(chunk)<=450),"Segmentation lost or oversized text");
    Check(chunks.All(chunk=>!char.IsLowSurrogate(chunk[0])&&!char.IsHighSurrogate(chunk[^1])),"An emoji was split");return Task.CompletedTask;
});
await Test("Translation encodes content, has no account token and caches the language pair",async()=>
{
    var calls=0;using var client=new TranslationClient(new Handler(request=>
    {
        var uri=request.RequestUri!;calls++;Check(request.Method==HttpMethod.Get&&uri.Host=="api.mymemory.translated.net","Wrong endpoint");
        Check(request.Headers.Authorization==null&&!uri.Query.Contains("token"),"Account credential leaked");
        Check(uri.Query.Contains("%26")&&uri.Query.Contains("%7C"),"Text or pair not encoded");
        return Reply(new {responseStatus=200,responseData=new {translatedText="读者 &amp; 学习"}});
    }));
    var result=await client.TranslateAsync("reader & learning");Check(result=="读者 & 学习","HTML entities not decoded");
    Check(await client.TranslateAsync("reader & learning")==result&&calls==1,"Cache missed");
    await client.TranslateAsync("reader & learning","zh-CN|en");Check(calls==2,"Language pair cache collided");
});
await Test("Quota exhaustion does not become a fake translation",async()=>
{
    using var client=new TranslationClient(new Handler(_=>Reply(new {responseStatus=403,responseData=new {translatedText="quota"}})));
    try{await client.TranslateAsync("A reader.");throw new Exception("Quota error accepted");}catch(TranslationException error){Check(error.Message.Contains("额度"),"Quota feedback missing");}
});
await Test("DeepL has priority, sends whole paragraphs by POST and isolates cache by credential and direction",async()=>
{
    var secret="00000000-0000-0000-0000-000000000000:fx";var calls=0;
    using var client=new TranslationClient(new Handler(request=>
    {
        calls++;Check(request.Method==HttpMethod.Post&&request.RequestUri!.Host=="api-free.deepl.com"&&request.RequestUri.AbsolutePath=="/v2/translate","DeepL priority or free endpoint wrong");
        Check(request.Headers.Authorization?.Scheme=="DeepL-Auth-Key"&&request.Headers.Authorization.Parameter==secret&&!request.RequestUri!.AbsoluteUri.Contains(secret),"DeepL auth was missing or leaked into URL");
        using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Check(body.RootElement.GetProperty("text").GetArrayLength()==1&&body.RootElement.GetProperty("text")[0].GetString()!.Contains('\n'),"Paragraph context was split before DeepL");
        Check(!body.RootElement.ToString().Contains(secret),"Key leaked into the text body");
        return Reply(new{translations=new[]{new{text="读者喜欢学习。\n这是第二段。",detected_source_language="EN"}}});
    }),()=>secret);
    var text="A reader enjoys learning.\nThis is a second paragraph.";
    Check((await client.TranslateAsync(text)).Contains("读者")&&client.LastProvider=="DeepL","DeepL translation not used");
    await client.TranslateAsync(text);Check(calls==1,"DeepL cache missed");
    secret="11111111-1111-1111-1111-111111111111:fx";await client.TranslateAsync(text);await client.TranslateAsync(text,"zh-CN|en");Check(calls==3,"Credential/direction caches collided");
});
await Test("DeepL authentication, quota and malformed data report explicit errors without exposing credentials",async()=>
{
    foreach(var code in new[]{403,456,200})
    {
        var calls=0;var secret="00000000-0000-0000-0000-000000000000";
        using var client=new TranslationClient(new Handler(request=>
        {
            calls++;Check(request.RequestUri!.Host=="api.deepl.com","Pro key reached the wrong endpoint or a fallback");
            return new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent(JsonSerializer.Serialize(new{message=secret,translations=new object[]{}}))};
        }),()=>secret);
        try{await client.TranslateAsync("A reader.");throw new Exception("Invalid DeepL response became a translation");}
        catch(TranslationException error){Check(calls==1&&!error.ToString().Contains(secret),"DeepL retried another provider or exposed a credential");}
    }
});
await Test("DeepL connection and temporary service failures automatically use a credential-free backup",async()=>
{
    foreach(var code in new[]{0,408,429,500,503})
    {
        var primary=0;var backup=0;const string secret="00000000-0000-0000-0000-000000000000:fx";
        using var client=new TranslationClient(new Handler(request=>
        {
            if(request.RequestUri!.Host=="api-free.deepl.com")
            {
                primary++;if(code==0)throw new HttpRequestException("Network unavailable");
                return new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent("{}")};
            }
            backup++;Check(request.RequestUri.Host=="api.mymemory.translated.net"&&request.Headers.Authorization==null&&!request.RequestUri.AbsoluteUri.Contains(secret),"A key leaked into the backup request");
            return Reply(new{responseStatus=200,responseData=new{translatedText="备用译文"}});
        }),()=>secret);
        Check(await client.TranslateAsync("A reader.")=="备用译文"&&client.LastProvider=="MyMemory（DeepL 暂不可用）","Connection failure did not use or identify the backup");
        await client.TranslateAsync("A different reader.");Check(primary==1&&backup==2,"A failed primary was retried during the short cooldown");
    }
});
await Test("DeepL recovery retries the preferred provider without polluting its translation cache",async()=>
{
    var primary=0;var backup=0;
    using var client=new TranslationClient(new Handler(request=>
    {
        if(request.RequestUri!.Host=="api-free.deepl.com")return ++primary==1?new HttpResponseMessage(HttpStatusCode.BadGateway){Content=new StringContent("{}")}:
            Reply(new{translations=new[]{new{text="首选译文"}}});
        backup++;return Reply(new{responseStatus=200,responseData=new{translatedText="备用译文"}});
    }),()=>"00000000-0000-0000-0000-000000000000:fx",retryCooldown:TimeSpan.Zero);
    Check(await client.TranslateAsync("A reader.")=="备用译文","Initial temporary outage did not fall back");
    Check(await client.TranslateAsync("A reader.")=="首选译文"&&client.LastProvider=="DeepL"&&primary==2&&backup==1,"Backup result masked a recovered primary");
});
await Test("A DeepL timeout uses the backup while caller cancellation stops all translation",async()=>
{
    foreach(var userCancel in new[]{false,true})
    {
        var backup=0;using var cancel=new CancellationTokenSource();
        using var client=new TranslationClient(new AsyncHandler(async(request,token)=>
        {
            if(request.RequestUri!.Host=="api-free.deepl.com")
            {
                if(userCancel)cancel.Cancel();
                await Task.Delay(Timeout.Infinite,token);throw new Exception("Delay completed");
            }
            backup++;return Reply(new{responseStatus=200,responseData=new{translatedText="备用译文"}});
        }),()=>"00000000-0000-0000-0000-000000000000:fx",deepLTimeout:TimeSpan.FromMilliseconds(50));
        try
        {
            var result=await client.TranslateAsync("A reader.",cancellationToken:cancel.Token);
            Check(!userCancel&&result=="备用译文"&&backup==1,"Timeout did not fall back exactly once");
        }
        catch(OperationCanceledException){Check(userCancel&&backup==0,"Canceled translation sent text to the backup");}
    }
});
await Test("Long Chinese paragraphs retain word boundaries in translated English",async()=>
{
    var calls=0;using var client=new TranslationClient(new Handler(_=>Reply(new {responseStatus=200,responseData=new {translatedText=++calls==1?"reader":"learns"}})));
    Check(await client.TranslateAsync(new string('读',180),"zh-CN|en")=="reader learns","Translated chunks concatenated English words");
});
await Test("Canceling a translation stops further segments",async()=>
{
    var calls=0;using var cancel=new CancellationTokenSource();
    using var client=new TranslationClient(new Handler(_=>{calls++;cancel.Cancel();return Reply(new {responseStatus=200,responseData=new {translatedText="译文"}});}));
    try{await client.TranslateAsync(new string('a',1200),cancellationToken:cancel.Token);throw new Exception("Canceled translation completed");}catch(OperationCanceledException){Check(calls==1,"Cancellation sent another segment");}
});
await Test("Invalid translation sizes and pairs are rejected before HTTP",async()=>
{
    using var client=new TranslationClient(new Handler(_=>throw new Exception("Invalid request reached HTTP")));
    foreach(var input in new[]{"",new string('a',5001)})try{await client.TranslateAsync(input);throw new Exception("Invalid size accepted");}catch(ArgumentException){}
    try{await client.TranslateAsync("hello","xx|yy");throw new Exception("Invalid pair accepted");}catch(ArgumentException){}
});
await Test("Translated keyword highlighting uses exact longest meanings and preserves punctuation",()=>
{
    var terms=TextHighlights.ChineseMeanings("adj. 健谈的；爱讲话的");var text="这位健谈的读者爱讲话。";var parts=TextHighlights.Split(text,terms);
    Check(string.Concat(parts.Select(part=>part.Text))==text,"Highlighting lost text");
    Check(parts.Where(part=>part.Highlighted).Select(part=>part.Text).SequenceEqual(new[]{"健谈的","爱讲话"}),"Meaning spans are wrong");
    Check(!TextHighlights.Split("译文没有对应词。",terms).Any(part=>part.Highlighted),"An unrelated Chinese word was highlighted");return Task.CompletedTask;
});
await Test("Translation highlighting rejects another word's meaning and uncertain locations",()=>
{
    var definitions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){["business"]="n. 业务；生意；职责；公司",["role"]="n. 角色；职责",["company"]="n. 公司",["talkative"]="adj. 健谈的；爱讲话的"};
    string? Lookup(string word)=>definitions.GetValueOrDefault(word);
    var terms=TextHighlights.TranslationTerms("Learning about the business will help you understand your role.","摸清业务，理解自己的职责。","business",new HashSet<string>{"business"},Lookup);
    Check(terms.SetEquals(new[]{"业务"}),"Business incorrectly claimed the translation of role");
    Check(TextHighlights.TranslationTerms("The business has expanded.","这家公司扩大了。","business",new HashSet<string>{"business"},Lookup).SetEquals(new[]{"公司"}),"An unambiguous dictionary meaning was lost");
    Check(TextHighlights.TranslationTerms("The business has expanded.","业务变多，业务扩大了。","business",new HashSet<string>{"business"},Lookup).Count==0,"Repeated uncertain locations were guessed");
    Check(TextHighlights.TranslationTerms("The role matters.","这项职责很重要。","business",new HashSet<string>{"business"},Lookup).Count==0,"Translation marked a word absent from the source");
    Check(TextHighlights.TranslationTerms("The business has expanded.","业务扩大了。","business",new HashSet<string>{"business"},Lookup,aligned:false).Count==0,"A whole-context fallback was treated as aligned");
    Check(TextHighlights.TranslationTerms("A talkative reader.","一个健谈的读者。","talkative",new HashSet<string>{"talkative"},Lookup).SetEquals(new[]{"健谈的","健谈"}),"Reliable direct highlights were lost");
    return Task.CompletedTask;
});
await Test("Spelling similarity supports insertions, deletions and transpositions",()=>
{
    Check(RelatedWords.SpellingDistance("form","from",2)==1,"Transposition distance wrong");
    Check(RelatedWords.SpellingDistance("reader","readers",2)==1&&RelatedWords.SpellingDistance("reader","rider",2)==2,"Insertion/deletion distance wrong");
    Check(RelatedWords.SpellingDistance("reader","banana",2)>2,"Unrelated spelling accepted");return Task.CompletedTask;
});
await Test("The shipped WordNet dataset returns documented relations with dictionary meanings",async()=>
{
    var path=Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??new[]{"outputs/MemoLex/data","../MemoLex/data","data"}.FirstOrDefault(value=>File.Exists(Path.Combine(value,"related.data")))??throw new Exception("Set MEMOLEX_DATA_DIR to the complete runtime data folder.");
    using var dictionary=new LocalDictionary(path);await dictionary.LoadAsync();
    using var relations=new RelatedWords(dictionary,path);await relations.LoadAsync();
    var talkative=await relations.LookupAsync("talkative");var happy=await relations.LookupAsync("happy");
    Check(talkative.Synonyms.Any(item=>item.Word=="loquacious"),"Known synonym missing");
    Check(happy.Derivatives.Any(item=>item.Word=="happiness"),"Known derivative missing");
    Check(talkative.Synonyms.All(item=>item.Meaning.Length>0)&&happy.Derivatives.All(item=>item.Meaning.Length>0),"Related meanings missing");
    Check((await relations.LookupAsync("form")).Similar.Any(item=>item.Word=="from"),"Common spelling neighbour missing");
});
await Test("Shipped phrases provide Chinese meanings, whole-word matches and inflected forms",async()=>
{
    var path=Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??"outputs/MemoLex/data";
    using var dictionary=new LocalDictionary(path);await dictionary.LoadAsync();using var relations=new RelatedWords(dictionary,path);await relations.LoadAsync();
    var timer=System.Diagnostics.Stopwatch.StartNew();
    var take=await relations.LookupAsync("take");var taken=await relations.LookupAsync("taken");var afloat=await relations.LookupAsync("afloat");var production=await relations.LookupAsync("production");
    var sense=await relations.LookupAsync("sense");var place=await relations.LookupAsync("place");
    Check(sense.Phrases.Any(item=>item.Word=="make sense"&&item.Meaning.Contains("有道理")&&item.Meaning.Contains("讲得通"))&&sense.Phrases.Any(item=>item.Word=="make sense of"),"Common non-prepositional idiom was filtered out: "+string.Join(", ",sense.Phrases.Select(item=>item.Word)));
    Check(place.Phrases.Any(item=>item.Word=="take place")&&!sense.Phrases.Any(item=>item.Word=="sense of humor"),"Common verb phrase was lost or a noun combination was included");
    Check(take.Phrases.Any(item=>item.Word=="take off"&&item.Meaning.Contains("起飞")),"Known phrase missing: "+string.Join(", ",take.Phrases.Select(item=>item.Word)));
    Check(taken.Phrases.Any(item=>item.Word=="take off"),"The base form did not supply relevant phrases: "+string.Join(", ",taken.Phrases.Select(item=>item.Word)));
    Check(!afloat.Phrases.Any(item=>item.Word=="keep afloat")&&!production.Phrases.Any(item=>item.Word is "production line" or "mass production"),"A technical-only phrase or ordinary noun combination entered the results");
    Check(!production.Phrases.Any(item=>item.Word is "cost of production" or "factors of production" or "line of production" or "means of production")&&!(await relations.LookupAsync("point")).Phrases.Any(item=>item.Word=="point of view"),"An internal preposition made a noun group look like a prepositional collocation");
    Check((await relations.LookupAsync("talk")).Phrases.Any(item=>item.Word=="talk about")&&(await relations.LookupAsync("spite")).Phrases.Any(item=>item.Word=="in spite of"),"Prepositional collocations are missing");
    Check(new[]{take,taken,afloat,production}.All(group=>group.Phrases.Count<=24&&group.Phrases.All(item=>item.Meaning==dictionary.Lookup(item.Word)?.translation&&item.Meaning.Any(c=>c is >= '\u3400' and <= '\u9fff'))),"A phrase meaning was missing, invented or unbounded");
    Console.WriteLine($"Phrase queries including initial index: {timer.ElapsedMilliseconds} ms; take: {string.Join(", ",take.Phrases.Select(item=>item.Word))}");
});
await Test("Phrases work without WordNet, exclude substring matches and honor cancellation",async()=>
{
    var root=Path.Combine(Path.GetTempPath(),"MemoLex.PhraseTests."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    try
    {
        using(var data=File.Create(Path.Combine(root,"dictionary.data")))
        using(var index=new StreamWriter(Path.Combine(root,"dictionary.index.tsv"),false,new UTF8Encoding(false)))
        {
            foreach(var (word,meaning,forms) in new[]{("talk","v. 谈话","3:talks"),("talks","谈话","0:talk"),("talk about","谈论",""),("small talk","闲谈",""),("talkative person","健谈的人",""),("talk data","English only",""),("talk Research","专名",""),("take","vt. 拿；取",""),("take off","脱下；起飞",""),("take off coat","脱下外衣","")})
            {
                var bytes=JsonSerializer.SerializeToUtf8Bytes(new DictionaryEntry{word=word,translation=meaning,exchange=forms});
                index.WriteLine($"{word}\t{data.Position}\t{bytes.Length}");data.Write(bytes);
            }
        }
        using var dictionary=new LocalDictionary(root);await dictionary.LoadAsync();using var relations=new RelatedWords(dictionary,root);await relations.LoadAsync();
        Check(!relations.Available,"The fixture unexpectedly loaded WordNet");
        var talk=await relations.LookupAsync("talk");var talks=await relations.LookupAsync("talks");var phrase=await relations.LookupAsync("take off");
        Check(talk.Phrases.Select(item=>item.Word).ToHashSet().SetEquals(new[]{"talk about"}),"A noun phrase, partial word, proper name or untranslated phrase entered the results");
        Check(talks.Phrases.Any(item=>item.Word=="talk about")&&phrase.Phrases.Count==1&&phrase.Phrases[0].Word=="take off coat","Inflection or exact contiguous phrase matching failed");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try{await relations.LookupAsync("talk",cancellation.Token);throw new Exception("A canceled phrase search completed");}catch(OperationCanceledException){}
    }
    finally{var resolved=Path.GetFullPath(root);if(resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("MemoLex.PhraseTests."))Directory.Delete(resolved,true);}
});
await Test("Chinese reverse search ranks common meanings and finds synonym alternatives",async()=>
{
    var path=Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??"outputs/MemoLex/data";
    using var dictionary=new LocalDictionary(path);await dictionary.LoadAsync();using var reverse=new ChineseReverseSearch(dictionary,path);await reverse.LoadAsync();
    var benchmarks=new List<object>();
    foreach(var query in new[]{"快乐","健谈","聪明","安全","满意","不快乐","目的地","管理","爱讲话","大"})
    {
        // The complete-match group can span the first page; synonym alternatives
        // remain available through the same "more results" pages as the UI.
        var clock=System.Diagnostics.Stopwatch.StartNew();var page=await reverse.SearchAsync(query,120);
        benchmarks.Add(new {query,milliseconds=clock.ElapsedMilliseconds,total=page.Total,top=page.Items.Take(12).Select(item=>new{item.Word,item.MatchedMeaning,item.Kind,item.Score}).ToArray()});
        if(query=="快乐")Check(page.Items.Take(12).Any(item=>item.Word=="happy")&&page.Items.Any(item=>item.Word is "glad" or "joyful" or "cheerful"),"Happy synonyms missing");
        if(query is "健谈" or "爱讲话")Check(page.Items.Take(12).Any(item=>item.Word=="talkative")&&page.Items.Any(item=>item.Word=="loquacious"),"Talkative synonyms missing");
        if(query=="聪明")Check(page.Items.Take(12).Any(item=>item.Word is "clever" or "intelligent"),"Smart meaning missing");
        if(query=="安全")Check(!page.Items.Any(item=>item.Word is "unsafe" or "insecure"),"Safety search included antonyms");
        if(query=="满意")Check(!page.Items.Any(item=>item.Word is "dissatisfied" or "unsatisfied"),"Satisfied search included antonyms");
        if(query=="不快乐")Check(page.Items.Any(item=>item.Word=="unhappy")&&!page.Items.Any(item=>item.Word=="happy"),"Negative meaning was lost");
        if(query=="目的地")Check(page.Items.Take(12).Any(item=>item.Word=="destination"),"Destination normalization failed");
        if(query=="大")Check(page.Items.Take(12).Any(item=>item.Word is "big" or "large"),"Single-character adjective search failed");
    }
    var report=Path.GetFullPath("work/MemoLex/chinese-search-checks.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    File.WriteAllText(report,JsonSerializer.Serialize(benchmarks,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    try{await reverse.SearchAsync("管理",cancellationToken:cancelled.Token);throw new Exception("Canceled Chinese search completed");}catch(OperationCanceledException){}
    Check((await reverse.SearchAsync("的")).Items.Count==0,"Stop-word query returned unrelated entries");
});
await Test("A complete Chinese meaning ranks before partial matches and synonym expansion",async()=>
{
    var path=Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??"outputs/MemoLex/data";
    using var dictionary=new LocalDictionary(path);await dictionary.LoadAsync();using var reverse=new ChineseReverseSearch(dictionary,path);await reverse.LoadAsync();
    var page=await reverse.SearchAsync("生产的",120);
    var generative=page.Items.ToList().FindIndex(item=>item.Word=="generative");
    var firstOther=page.Items.ToList().FindIndex(item=>item.Kind!="完全匹配");
    Console.WriteLine("Production ranking: "+string.Join(", ",page.Items.Take(12).Select(item=>item.Word+" ["+item.Kind+"]")));
    Check(generative>=0&&(firstOther<0||generative<firstOther),"Generative is not in the leading complete-match group");
    Check(page.Items[generative].MatchedMeaning=="生产的","The exact meaning was replaced by a different sense");
    foreach(var word in new[]{"production","productive"})
    {
        var index=page.Items.ToList().FindIndex(item=>item.Word==word);
        Check(index<0||index>generative,word+" outranks a complete matching meaning");
    }
    foreach(var query in new[]{"快乐","安全","生产的","生产"})
    {
        var matches=(await reverse.SearchAsync(query,120)).Items;
        var seenOther=false;
        foreach(var item in matches)
        {
            if(item.Kind!="完全匹配")seenOther=true;
            else Check(!seenOther,"Complete match appears after a partial match for "+query);
        }
    }
});
if(args.Contains("--live"))await Test("The public paragraph translation service accepts a real nonprivate example",async()=>
{
    using var client=new TranslationClient();var result=await client.TranslateAsync("A reader enjoys learning.");
    Check(result.Any(character=>character is >= '\u3400' and <= '\u9fff'),"Live translation did not return Chinese");
});
Console.WriteLine($"{passed} feature checks passed; translation HTTP was simulated except the explicit --live example.");
return;

static HttpResponseMessage Reply(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();return Task.FromResult(respond(request));}
}
sealed class AsyncHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>respond(request,cancellationToken);
}
