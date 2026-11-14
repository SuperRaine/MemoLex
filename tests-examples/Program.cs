using System.Net;
using System.Text;
using System.Text.Json;
using MemoLex.Core;
using MemoLex.Services;

if (args.Contains("--live"))
{
    using var client = new ExampleSentenceClient();
    foreach (var filter in PaperTypeChoice.All)
    {
        var page = await client.SearchAsync("talkative", filter.Value);
        if (page.Total <= 0 || page.Items.Count == 0 || page.Items.Any(item => item.English.Length == 0 || item.Source.Length == 0)) throw new Exception("Live search returned no usable examples");
        Console.WriteLine($"LIVE {filter.Value}: total={page.Total}, returned={page.ReceivedCount}, translated={page.Items.Count(item => item.Translation.Length > 0)}");
    }
    var empty = await client.SearchAsync("qzxqzxqzx");
    if (empty.Total != 0 || empty.Items.Count != 0 || empty.HasMore) throw new Exception("Live empty search not empty");
    Console.WriteLine("LIVE empty search: correct. Public lookup only; no token read or sent.");
    return 0;
}

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
HttpResponseMessage Reply(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { success = true, errors = Array.Empty<object>(), data }), Encoding.UTF8, "application/json") };
object Phrase(string id = "p1") => new { phrase_id = id, phrase_en = "A talkative student enjoys learning.", phrase_zh = "", source = "测试来源", context = new { phrase = "A ***talkative*** student enjoys learning.//", translation = "一个健谈的学生喜欢学习。//", options = Array.Empty<object>() }, highlight = new { context = new { phrase = "A <span class=\"bold\">talkative</span> student enjoys learning." } } };

await Test("Public lookup encodes the keyword and sends no bearer token", async () =>
{
    using var fake = new ProbeHandler((request, _) =>
    {
        Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "lookup.maimemo.com", "Wrong lookup destination");
        Check(request.Headers.Authorization == null, "Authorization sent to public search");
        Check(request.RequestUri!.Query.Contains("keyword=ice%20cream%26paper_type%3DALL") && request.RequestUri.Query.EndsWith("paper_type=CET6"), "Keyword changed the query parameters");
        return Task.FromResult(Reply(new { total = new { value = 1, relation = "eq" }, phrases = new[] { Phrase() } }));
    });
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    await client.SearchAsync("ice cream&paper_type=ALL", "CET6");
});
await Test("The supplied five paper-type values are used exactly", async () =>
{
    var seen = new List<string>();
    using var fake = new ProbeHandler((request, _) => { seen.Add(request.RequestUri!.Query.Split("paper_type=")[1]); return Task.FromResult(Reply(new { total = 0, phrases = Array.Empty<object>() })); });
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    foreach (var type in PaperTypeChoice.All) await client.SearchAsync("word", type.Value);
    Check(seen.SequenceEqual(new[] { "ALL", "POSTGRADUATE", "CET4", "CET6", "COLLEGE_ENTRANCE_EXAM" }), "Missing or renamed exam type");
});
await Test("Empty phrase_zh uses context translation and strips display markers", async () =>
{
    using var fake = new ProbeHandler((_, _) => Task.FromResult(Reply(new { total = new { value = 1 }, phrases = new[] { Phrase() } })));
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    var item = (await client.SearchAsync("talkative")).Items.Single();
    Check(item.Translation == "一个健谈的学生喜欢学习。" && !item.IsOption, "Translation fallback failed");
    Check(item.MatchedWords.SetEquals(new[] { "talkative" }), "Highlight parsing failed");
});
await Test("An option keeps its own translation and the blank question separate", async () =>
{
    var option = new { phrase_id = "option", source = "测试原题 选项[A]", phrase_en = "likely", phrase_zh = "", context = new { phrase = "The answer is |3|.", translation = "原题参考译文", answers = new[] { "B" }, options = new[] { new { option = "A", phrase = "likely", translation = "可能的" }, new { option = "B", phrase = "unlikely", translation = "不大可能的" } } } };
    using var fake = new ProbeHandler((_, _) => Task.FromResult(Reply(new { total = 1, phrases = new[] { option } })));
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    var item = (await client.SearchAsync("likely")).Items.Single();
    Check(item.IsOption && item.OptionLabel == "A" && item.English == "likely", "Option not labelled");
    Check(item.ContextEnglish == "The answer is ____." && !item.ContextEnglish.Contains("likely"), "Incorrect option inserted as a factual example");
    Check(item.Translation == "可能的" && item.ContextTranslation == "原题参考译文", "Option and question translations mixed");
});
await Test("Explicit phrase translation takes priority and context is a text fallback", async () =>
{
    var phrase = new { phrase_id = "fallback", phrase_en = "", phrase_zh = "专门的译文", source = "测试", context = new { phrase = "***A &amp; B***<br>learn.//", translation = "原题译文" } };
    using var fake = new ProbeHandler((_, _) => Task.FromResult(Reply(new { total = 1, phrases = new[] { phrase } })));
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    var item = (await client.SearchAsync("learn")).Items.Single();
    Check(item.English == "A & B\nlearn." && item.Translation == "专门的译文", "Text/translation fallback failed");
});
await Test("A whole-context translation is not assumed aligned with a shorter example",async()=>
{
    var phrase=new {phrase_id="partial",phrase_en="The business grew.",phrase_zh="",source="测试",context=new {phrase="They discussed roles. The business grew.",translation="他们讨论职责，公司发展了。"}};
    using var fake=new ProbeHandler((_,_)=>Task.FromResult(Reply(new {total=1,phrases=new[]{phrase}})));
    using var client=new ExampleSentenceClient(fake,TimeSpan.Zero);
    var item=(await client.SearchAsync("business")).Items.Single();
    Check(item.Translation=="他们讨论职责，公司发展了。"&&!item.TranslationAligned,"Context fallback was treated as an exact sentence alignment");
});
await Test("Cache distinguishes query, paper type and page, preserving pagination", async () =>
{
    var calls = 0;
    using var fake = new ProbeHandler((request, _) =>
    {
        calls++;
        var lastPage = request.RequestUri!.Query.Contains("offset=10");
        return Task.FromResult(Reply(new { total = 11, phrases = new[] { Phrase(lastPage ? "last" : "first") } }));
    });
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    var first = await client.SearchAsync("word", "CET6");
    await client.SearchAsync("WORD", "CET6");
    var last = await client.SearchAsync("word", "CET6", offset: 10);
    await client.SearchAsync("word", "CET4");
    await client.SearchAsync("another", "CET4");
    Check(calls == 4 && first.HasMore && !last.HasMore && last.Offset == 10 && last.ReceivedCount == 1, "Cache key or page boundary wrong");
});
await Test("A zero-result page is successful and has no next page", async () =>
{
    using var fake = new ProbeHandler((_, _) => Task.FromResult(Reply(new { total = new { value = 0 }, phrases = Array.Empty<object>() })));
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    var page = await client.SearchAsync("absentword");
    Check(page.Items.Count == 0 && !page.HasMore && page.Total == 0, "Empty results treated as an error");
});
await Test("Cancellation interrupts an old request and does not poison the cache", async () =>
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    using var fake = new ProbeHandler(async (_, token) =>
    {
        calls++;
        if (calls == 1) { started.SetResult(); await Task.Delay(Timeout.Infinite, token); }
        return Reply(new { total = 1, phrases = new[] { Phrase() } });
    });
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    using var cancel = new CancellationTokenSource();
    var old = client.SearchAsync("word", cancellationToken: cancel.Token);
    await started.Task; cancel.Cancel();
    try { await old; throw new Exception("Old request completed instead of cancelling"); } catch (OperationCanceledException) { }
    Check((await client.SearchAsync("word")).Items.Count == 1 && calls == 2, "Cancelled response reused");
});
await Test("HTTP errors and invalid responses produce controlled messages", async () =>
{
    foreach (var mode in new[] { "429", "500", "json", "envelope", "missing" })
    {
        using var fake = new ProbeHandler((_, _) => Task.FromResult(mode switch {
            "429" => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("upstream-private-detail") },
            "500" => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("upstream-private-detail") },
            "json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{broken-upstream-private-detail") },
            "envelope" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":false,\"errors\":[\"upstream-private-detail\"]}") },
            _ => Reply(new { total = 0 }) }));
        using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
        try { await client.SearchAsync("word"); throw new Exception("Invalid response accepted: " + mode); }
        catch (ExampleLookupException error) { Check(!error.Message.Contains("upstream-private-detail"), "Upstream response exposed"); }
    }
});
await Test("Invalid filters and paging are rejected before sending HTTP", async () =>
{
    var calls = 0;
    using var fake = new ProbeHandler((_, _) => { calls++; return Task.FromResult(Reply(new { total = 0, phrases = Array.Empty<object>() })); });
    using var client = new ExampleSentenceClient(fake, TimeSpan.Zero);
    foreach (var task in new[] { client.SearchAsync("word", "CET8"), client.SearchAsync("word", offset: -1), client.SearchAsync("word", limit: 100), client.SearchAsync(" ") })
        try { await task; throw new Exception("Invalid input accepted"); } catch (ArgumentException) { }
    Check(calls == 0, "Invalid input reached HTTP");
});
await Test("Word links preserve punctuation and handle apostrophes, accents and hyphens", () =>
{
    var text = "“Can’t a teacher’s naïve, long-term idea work in 2027?”";
    var parts = ExampleText.Tokenize(text);
    Check(string.Concat(parts.Select(part => part.Text)) == text, "Sentence changed during tokenization");
    var words = parts.Where(part => part.IsWord).Select(part => part.LookupWord).ToArray();
    Check(words.Contains("Can't") && words.Contains("teacher's") && words.Contains("naive"), "Lookup normalization failed");
    Check(words.Contains("long") && words.Contains("term") && !words.Contains("2027"), "Hyphenated words or numbers are wrong");
    Check(ExampleText.ResolveLookupWord("teacher’s", word => word == "teacher") == "teacher", "Possessive base lookup failed");
    Check(ExampleText.ResolveLookupWord("it's", word => word == "it's") == "it's", "Known contraction changed");
    return Task.CompletedTask;
});
Console.WriteLine($"{passed} example and word-link checks passed. All HTTP was simulated.");
return 0;

sealed class ProbeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}
