using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoLex.Services;

var fixture = new BackendTestFixture();
try { return await fixture.RunAsync(); }
finally { fixture.Dispose(); }

sealed class BackendTestFixture : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MemoLex.BackendTests." + Guid.NewGuid().ToString("N"));
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private int _passed;
    private int _failed;
    public TokenStore Tokens { get; }

    public BackendTestFixture()
    {
        Directory.CreateDirectory(_folder);
        Tokens = new TokenStore(Path.Combine(_folder, "token.dpapi"));
    }

    public async Task<int> RunAsync()
    {
        await Test("DPAPI round trip is encrypted and isolated", () =>
        {
            Check(!Tokens.HasToken, "Expected an empty isolated token store");
            Tokens.SaveToken(_token);
            Check(Tokens.GetToken() == _token, "DPAPI token round trip failed");
            var file = File.ReadAllBytes(Path.Combine(_folder, "token.dpapi"));
            Check(!Encoding.UTF8.GetString(file).Contains(_token, StringComparison.Ordinal), "Plaintext credential found");
            Check(Path.GetFullPath(Path.Combine(_folder, "token.dpapi")) != Path.GetFullPath(TokenStore.TokenPath), "Test used the live token store");
            Tokens.DeleteToken();
            Check(!Tokens.HasToken, "Delete did not clear isolated token");
            Tokens.SaveToken(_token);
            return Task.CompletedTask;
        });

        await Test("Vocabulary unwraps real API envelope and escapes query", async () =>
        {
            using var fake = new FakeHandler(request =>
            {
                Check(request.Method == HttpMethod.Get, "Expected GET");
                Check(request.RequestUri!.Query.Contains("ice%20cream"), "Query was not URL-escaped");
                Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == _token, "Missing bearer header");
                Check(request.RequestUri.Host == "open.maimemo.com", "Wrong credential destination");
                Check(!request.RequestUri.AbsoluteUri.Contains(_token), "Token leaked into URL");
                return Reply("""{"errors":[],"data":{"voc":{"id":1234,"spelling":"ice cream"}},"success":true}""");
            });
            using var api = Client(fake);
            var word = await api.ResolveWordAsync("ice cream");
            Check(word is { Id: "1234", Spelling: "ice cream" }, "Envelope/numeric id parsing failed");
        });

        await Test("401 and API errors never echo sensitive upstream bodies", async () =>
        {
            foreach (var mode in new[] { "401", "api" })
            {
                using var fake = new FakeHandler(_ => mode == "401"
                    ? Reply("credential=" + _token, HttpStatusCode.Unauthorized)
                    : Reply(JsonSerializer.Serialize(new { success = false, errors = new[] { new { code = "TOKEN_INVALID", message = _token } } })));
                using var api = Client(fake);
                try { await api.ResolveWordAsync("apple"); throw new Exception("Expected auth error"); }
                catch (MaiMemoException error)
                {
                    Check(error.StatusCode == HttpStatusCode.Unauthorized, "Wrong authentication classification");
                    Check(!error.ToString().Contains(_token), "Credential leaked in exception");
                    Check(error.Message.Contains("Token"), "Missing actionable auth error");
                }
            }
        });

        await Test("Today new-word addition uses the documented memo route and verifies the server new-word flag", async () =>
        {
            using var fake=new StudyHandler();
            fake.CheckRequest=request=>
            {
                Check(request.RequestUri!.Host=="open.maimemo.com"&&request.Headers.Authorization?.Parameter==_token,"Wrong credential destination");
                if(!request.RequestUri.AbsolutePath.EndsWith("/add_words"))return;
                using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var root=body.RootElement;
                Check(request.Method==HttpMethod.Post&&request.RequestUri.AbsolutePath=="/open/api/v1/memo/study/add_words","Wrong study path or method");
                Check(root.GetProperty("words").GetArrayLength()==1&&root.GetProperty("words")[0].GetProperty("id").GetString()=="v1"&&root.GetProperty("advance").GetBoolean(),"Wrong add/advance body");
                Check(!root.ToString().Contains(_token),"Token leaked into the request body");
            };
            using var api=Client(fake);var result=await api.AddToTodayAsync(new("v1","apple"));
            Check(result==new TodayStudyResult(TodayStudyStatus.Added,1)&&fake.Adds==1,"Addition was not verified");
            Check(fake.Paths.SequenceEqual(new[]{"get_today_items","query_study_records","add_words","get_today_items","query_study_records"}),"Unexpected write, retry or read sequence");
        });
        await Test("Today new-word membership and existing review words are identified without writes",async()=>
        {
            foreach(var isNew in new[]{true,false})
            {
                using var fake=new StudyHandler {InitiallyToday=true,IsNew=isNew,StudyCount=isNew?null:3};using var api=Client(fake);
                var result=await api.AddToTodayAsync(new("v1","apple"));
                Check(result.Status==(isNew?TodayStudyStatus.AlreadyToday:TodayStudyStatus.AlreadyLearned)&&fake.Adds==0,"Existing today word was changed");
            }
        });
        await Test("Actual study counts distinguish planned words from learned words even with a first-study date",async()=>
        {
            foreach(var count in new[]{0,3})
            {
                using var fake=new StudyHandler {StudyCount=count,FirstStudyDate=count==0?"2026-11-01T00:00:00+08:00":null};using var api=Client(fake);
                var result=await api.AddToTodayAsync(new("v1","apple"));
                Check(result.Status==(count==0?TodayStudyStatus.Planned:TodayStudyStatus.AlreadyLearned)&&fake.Adds==0,"Planning was confused with actual study");
            }
        });
        await Test("Zero accepted words never produce a successful today-new result",async()=>
        {
            using var fake=new StudyHandler {AddedCount=0};using var api=Client(fake);
            var result=await api.AddToTodayAsync(new("v1","apple"));
            Check(result.Status==TodayStudyStatus.NotAdded&&result.AddedCount==0&&fake.Adds==1,"A zero count became success");
        });
        await Test("A lost add response is checked once without repeating the study write",async()=>
        {
            using var fake=new StudyHandler {LoseAddResponse=true};using var api=Client(fake);
            var result=await api.AddToTodayAsync(new("v1","apple"));
            Check(result.Status==TodayStudyStatus.AlreadyToday&&fake.Adds==1&&fake.Paths.Count==5,"Uncertain addition was not resolved by readback");
        });
        await Test("Unconfirmed today status offers read-only verification even after the plan accepted the word",async()=>
        {
            using var fake=new StudyHandler {HideAfterAdd=true};using var api=Client(fake);
            var result=await api.AddToTodayAsync(new("v1","apple"));
            Check(result.Status==TodayStudyStatus.PendingVerification&&result.AddedCount==1,"An accepted count without any synced learning data was called unlearned");
            fake.StudyCount=0;
            Check((await api.AddToTodayAsync(new("v1","apple"),verifyOnly:true)).Status==TodayStudyStatus.Planned&&fake.Adds==1,"Verification repeated a write");
        });
        await Test("Incomplete, wrong-ID and malformed learning data never authorize an unsafe study write",async()=>
        {
            foreach(var mode in new[]{"missing-today","wrong-today","missing-record","wrong-record"})
            {
                using var fake=new StudyHandler {BadData=mode};using var api=Client(fake);
                try{await api.AddToTodayAsync(new("v1","apple"));throw new Exception("Incomplete study data accepted");}
                catch(MaiMemoException){Check(fake.Adds==0,"Bad preflight data reached a study write");}
            }
            using var malformed=new StudyHandler {BadData="bad-count",HideAfterAdd=true};using var malformedApi=Client(malformed);
            Check((await malformedApi.AddToTodayAsync(new("v1","apple"))).Status==TodayStudyStatus.PendingVerification&&malformed.Adds==1,"Malformed add count was accepted or retried");
        });
        await Test("Study permission rejection and caller cancellation do not trigger a write retry",async()=>
        {
            using var fake=new StudyHandler {Rejected=true};using var api=Client(fake);
            try{await api.AddToTodayAsync(new("v1","apple"));throw new Exception("Study permission error accepted");}
            catch(MaiMemoException error){Check(error.StatusCode==HttpStatusCode.Forbidden&&fake.Adds==1&&fake.Paths.Count==3,"A rejected study write was retried");}
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            var calls=fake.Paths.Count;
            try{await api.AddToTodayAsync(new("v1","apple"),cancellationToken:canceled.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){Check(fake.Paths.Count==calls,"Canceled study action sent HTTP");}
        });
        await Test("Concurrent today-add actions serialize and observe the first confirmed addition",async()=>
        {
            using var fake=new StudyHandler();using var api=Client(fake);
            var results=await Task.WhenAll(api.AddToTodayAsync(new("v1","apple")),api.AddToTodayAsync(new("v1","apple")));
            Check(results.Count(r=>r.Status==TodayStudyStatus.Added)==1&&results.Count(r=>r.Status==TodayStudyStatus.AlreadyToday)==1&&fake.Adds==1,"Concurrent actions added twice");
        });

        await Test("Learning status reads identify learned, today-new and unlearned words without an add request",async()=>
        {
            foreach(var mode in new[]{"learned","today","new","planned"})
            {
                using var fake=new StudyHandler {StudyCount=mode=="learned"?3:mode=="planned"?0:null,InitiallyToday=mode=="today"};using var api=Client(fake);
                var result=await api.GetTodayStudyStatusAsync(new("v1","apple"));
                Check(result==(mode=="learned"?TodayStudyStatus.AlreadyLearned:mode=="today"?TodayStudyStatus.AlreadyToday:mode=="planned"?TodayStudyStatus.Planned:TodayStudyStatus.NotAdded)&&fake.Adds==0,"Read-only status was wrong or changed learning data");
                Check(fake.Paths[0]=="query_study_records"&&(mode!="learned"||fake.Paths.Count==1),"Known learned status did not come directly from its record");
            }
        });
        await Test("Incomplete status data remains an error without inferring an unlearned word",async()=>
        {
            using var fake=new StudyHandler {BadData="missing-record"};using var api=Client(fake);
            try{await api.GetTodayStudyStatusAsync(new("v1","apple"));throw new Exception("Bad learning state became unlearned");}
            catch(MaiMemoException){Check(fake.Adds==0&&fake.Paths.Count==1,"A failed status query caused further actions");}
        });
        await Test("Permission-limited token errors do not invalidate an authenticated account",async()=>
        {
            using var fake=new FakeHandler(request=>request.RequestUri!.AbsolutePath.EndsWith("/vocabulary")?Envelope(new {voc=new {id="v1",spelling="apple"}}):Reply("""{"success":false,"errors":[{"code":"TOKEN_PERMISSION_DENIED"}]}"""));
            using var api=Client(fake);var accepted=0;var rejected=0;api.AuthenticationResult+=(_,ok)=>{if(ok)accepted++;else rejected++;};
            await api.ResolveWordAsync("apple");
            try{await api.GetNotesAsync("v1");throw new Exception("Permission error accepted");}
            catch(MaiMemoException error){Check(error.StatusCode==HttpStatusCode.Forbidden&&accepted==1&&rejected==0,"Resource permission was reported as expired login");}
        });
        await Test("An in-flight response from the previous account is discarded before authentication or caching",async()=>
        {
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var nextToken=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var events=new List<(string Key,bool Accepted)>();
            using var fake=new FakeHandler(async request=>
            {
                if(request.RequestUri!.AbsolutePath.EndsWith("/notes")){started.SetResult();await release.Task;return Reply("{}",HttpStatusCode.Unauthorized);}
                return Envelope(new {voc=new {id="v1",spelling="apple"}});
            });
            using var api=Client(fake);api.AuthenticationResult+=(key,ok)=>events.Add((key,ok));
            var old=api.GetNotesAsync("v1");await started.Task;Tokens.SaveToken(nextToken);var fresh=api.ResolveWordAsync("apple");release.SetResult();
            try{await old;throw new Exception("Expected old-account cancellation");}catch(AccountChangedException){}
            await fresh;
            Check(events.SequenceEqual(new[]{(TokenStore.Fingerprint(nextToken)!,true)}),"Auth events crossed credentials");
            Tokens.SaveToken(_token);
        });

        await Test("Normal notes, interpretations and phrases parse content", async () =>
        {
            using var fake = new FakeHandler(request => request.RequestUri!.AbsolutePath.Split('/').Last() switch
            {
                "notes" => Reply("""{"success":true,"data":{"notes":[{"id":"n1","note_type":"词根","note":"e + phēméra → 只活一天","status":"PUBLISHED"},{"id":"n2","note_type":"其他","note":"deleted","status":"DELETED"}]}}"""),
                "interpretations" => Reply("""{"success":true,"data":{"interpretations":[{"id":"i1","interpretation":"adj. 短暂的","tags":["考研"],"status":"UNPUBLISHED"}]}}"""),
                "phrases" => Reply("""{"success":true,"data":{"phrases":[{"id":"p1","phrase":"Fame is ephemeral.","interpretation":"名声是短暂的。","origin":"自编","status":"PUBLISHED"}]}}"""),
                _ => throw new Exception("Unexpected route")
            });
            using var api = Client(fake);
            var notes = await api.GetNotesAsync("123");
            Check(notes.Count == 1 && notes[0].NoteType == "词根" && notes[0].Text.Contains("一天"), "Normal note parsing failed");
            var definitions = await api.GetInterpretationsAsync("123");
            Check(definitions.Count == 1 && definitions[0].Status == "UNPUBLISHED" && definitions[0].Tags.Single() == "考研", "Private definition parsing failed");
            var phrases = await api.GetPhrasesAsync("123");
            Check(phrases[0].Text == "Fame is ephemeral." && phrases[0].Interpretation == "名声是短暂的。", "Phrase parsing failed");
        });

        await Test("Cloud lists paginate without duplicating or showing deleted/favorites", async () =>
        {
            var calls = 0;
            using var fake = new FakeHandler(request =>
            {
                calls++;
                var offset = calls == 1 ? 0 : 10;
                Check(request.RequestUri!.Query.Contains("offset=" + offset), "Wrong pagination offset");
                Check(request.RequestUri.Query.Contains("limit=10"), "Page size exceeds the upstream maximum of 10");
                var pads = calls == 1
                    ? Enumerable.Range(0, 10).Select(i => Pad(i.ToString(), i == 3 ? "DELETED" : "UNPUBLISHED", i == 4 ? "FAVORITE" : "NOTEPAD")).ToArray()
                    : new[] { Pad("9"), Pad("10") };
                return Envelope(new { notepads = pads });
            });
            using var api = Client(fake);
            var pads = await api.ListNotepadsAsync();
            Check(calls == 2 && pads.Count == 9, "Pagination count wrong");
            Check(pads.Any(x => x.Status == "UNPUBLISHED"), "Private word lists were lost");
            Check(pads.Select(x => x.Id).Distinct().Count() == pads.Count, "Duplicate word list");
        });

        await Test("Append preserves chapters, metadata and unpublished status", async () =>
        {
            var posted = false;
            const string existing = "# Chapter 1\r\napple\r\n# Chapter 2\r\nbanana";
            using var fake = new FakeHandler(async request =>
            {
                if (request.Method == HttpMethod.Get) return Envelope(new { notepad = Pad("p1", "UNPUBLISHED", content: existing) });
                posted = true;
                using var parsed = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var pad = parsed.RootElement.GetProperty("notepad");
                Check(pad.GetProperty("content").GetString() == existing + "\nserendipity", "Append destroyed existing chapters");
                Check(pad.GetProperty("title").GetString() == "原来的词本", "Title not preserved");
                Check(pad.GetProperty("brief").GetString() == "原来的介绍", "Brief not preserved");
                Check(pad.GetProperty("status").GetString() == "UNPUBLISHED", "Private list was published");
                Check(pad.GetProperty("tags")[0].GetString() == "考研", "Tags not preserved");
                return Envelope(new { notepad = Pad("p1", "UNPUBLISHED", content: existing + "\nserendipity") });
            });
            using var api = Client(fake);
            var result = await api.AddToNotepadAsync("p1", "serendipity");
            Check(posted && !result.AlreadyExisted && result.Notepad.Status == "UNPUBLISHED", "Append result incorrect");
        });

        await Test("Case-insensitive duplicates never trigger a write", async () =>
        {
            var calls = 0;
            using var fake = new FakeHandler(request =>
            {
                calls++;
                Check(request.Method == HttpMethod.Get, "Duplicate word triggered a write");
                return Envelope(new { notepad = Pad("p1", content: "# Letter A\nApple\nbanana\n") });
            });
            using var api = Client(fake);
            var result = await api.AddToNotepadAsync("p1", "apple");
            Check(calls == 1 && result.AlreadyExisted, "Duplicate not detected");
        });

        await Test("Explicit new notepad contains the current word in one write", async () =>
        {
            var calls = 0;
            using var fake = new FakeHandler(async request =>
            {
                calls++;
                Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/notepads"), "Wrong create route");
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var pad = json.RootElement.GetProperty("notepad");
                Check(pad.GetProperty("title").GetString() == "桌面生词本", "Create title wrong");
                Check(pad.GetProperty("content").GetString() == "ephemeral", "Initial word missing");
                Check(pad.GetProperty("status").GetString() == "UNPUBLISHED", "New cloud list was published");
                return Envelope(new { notepad = Pad("new", "UNPUBLISHED", content: "ephemeral") });
            });
            using var api = Client(fake);
            var created = await api.CreateNotepadAsync("桌面生词本", "ephemeral");
            Check(calls == 1 && created.Id == "new" && created.Status == "UNPUBLISHED", "Create result or private status incorrect");
        });

        await Test("Incomplete full notepad data aborts instead of overwriting", async () =>
        {
            var calls = 0;
            using var fake = new FakeHandler(request =>
            {
                calls++;
                Check(request.Method == HttpMethod.Get, "Incomplete content caused a write");
                return Envelope(new { notepad = new { id = "p1", title = "brief response", status = "UNPUBLISHED", tags = new[] { "考研" } } });
            });
            using var api = Client(fake);
            try { await api.AddToNotepadAsync("p1", "apple"); throw new Exception("Expected incomplete content error"); }
            catch (MaiMemoException error) { Check(error.Message.Contains("完整云词本") && calls == 1, "Incomplete notepad error missing"); }
        });

        await Test("Request queue enforces spacing and permits cancellation", async () =>
        {
            var times = new List<long>();
            using var fake = new FakeHandler(_ =>
            {
                times.Add(Stopwatch.GetTimestamp());
                return Envelope(new { voc = new { id = "1", spelling = "apple" } });
            });
            using var api = new MaiMemoClient(Tokens, fake, TimeSpan.FromMilliseconds(60));
            await Task.WhenAll(api.ResolveWordAsync("apple"), api.ResolveWordAsync("apple"));
            Check(times.Count == 2 && Stopwatch.GetElapsedTime(times[0], times[1]).TotalMilliseconds >= 50, "Pacing failed");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { await api.ResolveWordAsync("apple", cancellation.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { Check(times.Count == 2, "Canceled request still reached upstream"); }
        });

        await Test("Removing a word preserves chapters, line endings and metadata",async()=>
        {
            var original="# Chapter 1\r\nAPPLE\r\nbanana\r\n# apple notes\r\napple pie\r\n";var calls=0;
            using var fake=new FakeHandler(async request=>
            {
                calls++;
                if(request.Method==HttpMethod.Get)return Envelope(new {notepad=Pad("p1","UNPUBLISHED",content:original)});
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());var pad=body.RootElement.GetProperty("notepad");
                Check(pad.GetProperty("content").GetString()=="# Chapter 1\r\nbanana\r\n# apple notes\r\napple pie\r\n","Removal changed another word or heading");
                Check(pad.GetProperty("status").GetString()=="UNPUBLISHED"&&pad.GetProperty("tags")[0].GetString()=="考研","Removal changed metadata");
                return Envelope(new {notepad=Pad("p1","UNPUBLISHED",content:pad.GetProperty("content").GetString()!)});
            });
            using var api=Client(fake);var result=await api.RemoveFromNotepadAsync("p1","apple");
            Check(calls==2&&!result.AlreadyAbsent&&!MaiMemoClient.ContainsWord(result.Notepad,"apple"),"Removal result wrong");
        });
        await Test("Removing an absent word is idempotent and performs no write",async()=>
        {
            using var fake=new FakeHandler(request=>{Check(request.Method==HttpMethod.Get,"Absent removal wrote content");return Envelope(new {notepad=Pad("p1",content:"banana")});});
            using var api=Client(fake);Check((await api.RemoveFromNotepadAsync("p1","apple")).AlreadyAbsent,"Missing absent result");
        });
        await Test("Editing a word list updates requested fields and preserves tags and publishing",async()=>
        {
            using var fake=new FakeHandler(async request=>
            {
                if(request.Method==HttpMethod.Get)return Envelope(new {notepad=Pad("p1","UNPUBLISHED",content:"# One\napple")});
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());var value=body.RootElement.GetProperty("notepad");
                Check(value.GetProperty("title").GetString()=="新名称"&&value.GetProperty("brief").GetString()=="新简介"&&value.GetProperty("content").GetString()=="# One\nbanana","Edit did not use requested fields");
                Check(value.GetProperty("tags")[0].GetString()=="考研"&&value.GetProperty("status").GetString()=="UNPUBLISHED","Edit changed protected metadata");
                return Envelope(new {notepad=new {id="p1",title="新名称",brief="新简介",content="# One\nbanana",tags=new[]{"考研"},status="UNPUBLISHED",type="NOTEPAD"}});
            });
            using var api=Client(fake);var original=await api.GetNotepadAsync("p1");await api.UpdateNotepadAsync(original,"新名称","新简介","# One\nbanana");
        });
        await Test("Editing detects an intervening cloud change and never overwrites it",async()=>
        {
            var reads=0;using var fake=new FakeHandler(request=>
            {Check(request.Method==HttpMethod.Get,"Conflict caused an overwrite");return Envelope(new {notepad=Pad("p1",content:++reads==1?"apple":"apple\nremote-new-word")});});
            using var api=Client(fake);var original=await api.GetNotepadAsync("p1");
            try{await api.UpdateNotepadAsync(original,"新名称",original.Brief,"banana");throw new Exception("Expected conflict");}
            catch(MaiMemoException error){Check(error.Message.Contains("其他位置更新"),"Conflict feedback missing");}
        });
        await Test("Brief save responses refresh add and removal membership from submitted content",async()=>
        {
            foreach(var remove in new[]{false,true})
            {
                using var fake=new FakeHandler(request=>request.Method==HttpMethod.Get
                    ?Envelope(new {notepad=Pad("p1",content:remove?"apple\nbanana":"banana")})
                    :Envelope(new {notepad=new {id="p1",title="brief only",list=new[]{new {spelling="apple"}}}}));
                using var api=Client(fake);
                var saved=remove?(await api.RemoveFromNotepadAsync("p1","apple")).Notepad:(await api.AddToNotepadAsync("p1","apple")).Notepad;
                Check(MaiMemoClient.ContainsWord(saved,"apple")==!remove&&saved.HasContent,"Brief POST response kept stale membership");
                if(remove)Check(saved.Content=="banana"&&saved.Words.Count==1,"Removal lost the retained word");
            }
        });
        await Test("An explicit conflicting save response is rejected",async()=>
        {
            using var fake=new FakeHandler(request=>Envelope(new {notepad=Pad("p1",content:"banana")}));using var api=Client(fake);
            try{await api.AddToNotepadAsync("p1","apple");throw new Exception("Conflicting response accepted");}
            catch(MaiMemoException error){Check(error.Message.Contains("不一致"),"Conflicting save feedback missing");}
        });
        await Test("Mnemonic creation and editing use the documented POST bodies and retain returned identity",async()=>
        {
            foreach(var existing in new[]{false,true})
            {
                using var fake=new FakeHandler(async request=>
                {
                    Check(request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath.EndsWith(existing?"/notes/n1":"/notes"),"Wrong note save endpoint");
                    using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());var note=body.RootElement.GetProperty("note");
                    Check(note.GetProperty("note_type").GetString()=="词根词缀"&&note.GetProperty("note").GetString()=="talk + -ative","Note content changed");
                    Check(note.TryGetProperty("voc_id",out _)==!existing&&!note.TryGetProperty("status",out _),"Note body invented a status or changed the word on update");
                    return Envelope(new {note=new {id="n1",note_type="词根词缀",note="talk + -ative",status="PUBLISHED"}});
                });
                using var api=Client(fake);var saved=await api.SaveNoteAsync("v1",existing?"n1":null,"词根词缀","talk + -ative");Check(saved.Id=="n1","Cloud identity missing");
            }
        });
        await Test("Invalid mnemonic content never reaches HTTP",async()=>
        {
            using var fake=new FakeHandler((Func<HttpRequestMessage,HttpResponseMessage>)(_=>throw new Exception("Invalid note reached HTTP")));using var api=Client(fake);
            try{await api.SaveNoteAsync("v1",null,"词根"," ");throw new Exception("Empty note accepted");}catch(ArgumentException){}
        });
        await Test("Removing the final word and saving an empty list never issue a POST",async()=>
        {
            var calls=0;using var fake=new FakeHandler(request=>{calls++;Check(request.Method==HttpMethod.Get,"An empty notepad was posted");return Envelope(new {notepad=Pad("p1",content:"apple")});});
            using var api=Client(fake);var original=await api.GetNotepadAsync("p1");
            try{await api.RemoveFromNotepadAsync("p1","apple");throw new Exception("Final word removed");}catch(EmptyNotepadException error){Check(error.Notepad.Id=="p1","Missing confirmation snapshot");}
            try{await api.UpdateNotepadAsync(original,original.Title,original.Brief,"# Empty");throw new Exception("Empty list saved");}catch(EmptyNotepadException){}
            Check(calls==3,"Unexpected request count");
        });
        await Test("Whole-notepad deletion checks the current snapshot and uses DELETE with no body",async()=>
        {
            var deleted=false;using var fake=new FakeHandler(request=>
            {
                if(request.Method==HttpMethod.Get)return Envelope(new {notepad=Pad("p1",content:"apple")});
                Check(request.Method==HttpMethod.Delete&&request.Content==null&&request.RequestUri!.AbsolutePath.EndsWith("/notepads/p1"),"Wrong delete request");deleted=true;return Envelope(new {notepad=Pad("p1","DELETED",content:"apple")});
            });using var api=Client(fake);var original=await api.GetNotepadAsync("p1");var result=await api.DeleteNotepadAsync(original);Check(deleted&&result.Status=="DELETED","Deletion was not acknowledged");
        });
        await Test("A remotely changed cloud notepad cannot be deleted from an old confirmation snapshot",async()=>
        {
            var reads=0;using var fake=new FakeHandler(request=>{Check(request.Method==HttpMethod.Get,"A changed notepad was deleted");return Envelope(new {notepad=Pad("p1",content:++reads==1?"apple":"apple\nreader")});});
            using var api=Client(fake);var original=await api.GetNotepadAsync("p1");
            try{await api.DeleteNotepadAsync(original);throw new Exception("Cloud conflict ignored");}catch(MaiMemoException error){Check(error.Message.Contains("其他位置更新"),"Missing delete conflict feedback");}
        });
        await Test("A stale DELETE response is confirmed by a brief deleted readback",async()=>
        {
            var deletes=0;var reads=0;
            using var fake=new FakeHandler(request=>
            {
                Check(request.RequestUri!.AbsolutePath.EndsWith("/notepads/p1"),"Verification queried a different notepad");
                if(request.Method==HttpMethod.Delete){deletes++;return Envelope(new {notepad=Pad("p1",content:"apple")});}
                reads++;return deletes==0?Envelope(new {notepad=Pad("p1",content:"apple")}):Envelope(new {notepad=new {id="p1",status="DELETED"}});
            });
            using var api=Client(fake);var original=await api.GetNotepadAsync("p1");var result=await api.DeleteNotepadAsync(original);
            Check(result.Id=="p1"&&result.Status=="DELETED"&&deletes==1&&reads==3,"Readback did not acknowledge the successful delete exactly once");
        });
        await Test("Empty, brief, malformed and mismatched DELETE bodies recover through a missing-record readback",async()=>
        {
            foreach(var mode in new[]{"empty","brief","null","malformed","wrong-id"})
            {
                var deletes=0;var reads=0;
                using var fake=new FakeHandler(request=>
                {
                    if(request.Method==HttpMethod.Get){reads++;return deletes==0?Envelope(new {notepad=Pad("p1",content:"apple")}):new HttpResponseMessage(HttpStatusCode.NotFound);}
                    Check(request.Method==HttpMethod.Delete,"Recovery repeated a write");deletes++;
                    return mode switch
                    {
                        "empty"=>new HttpResponseMessage(HttpStatusCode.NoContent),
                        "brief"=>Envelope(new {notepad=new {id="p1"}}),
                        "null"=>Reply("""{"success":true,"data":null}"""),
                        "malformed"=>Reply("not-json"),
                        _=>Envelope(new {notepad=new {id="another-book",status="DELETED"}})
                    };
                });
                using var api=Client(fake);var result=await api.DeleteNotepadAsync(await api.GetNotepadAsync("p1"));
                Check(result.Id=="p1"&&result.Status=="DELETED"&&deletes==1&&reads==3,"Deletion recovery failed for "+mode);
            }
        });
        await Test("A connection loss or timeout after deletion verifies once without repeating DELETE",async()=>
        {
            foreach(var timeout in new[]{false,true})
            {
                var deletes=0;var reads=0;
                using var fake=new FakeHandler((Func<HttpRequestMessage,HttpResponseMessage>)(request=>
                {
                    if(request.Method==HttpMethod.Get){reads++;return deletes==0?Envelope(new {notepad=Pad("p1",content:"apple")}):new HttpResponseMessage(HttpStatusCode.NotFound);}
                    deletes++;
                    if(timeout)throw new TaskCanceledException("Simulated response timeout");
                    throw new HttpRequestException("Simulated response loss");
                }));
                using var api=Client(fake);var result=await api.DeleteNotepadAsync(await api.GetNotepadAsync("p1"));
                Check(result.Status=="DELETED"&&deletes==1&&reads==3,"A response loss repeated the destructive request");
            }
        });
        await Test("An active target is never reported deleted from an ambiguous or wrong-ID response",async()=>
        {
            var deletes=0;var reads=0;
            using var fake=new FakeHandler(request=>
            {
                if(request.Method==HttpMethod.Get){reads++;return Envelope(new {notepad=Pad("p1",content:"apple")});}
                deletes++;return Envelope(new {notepad=new {id="another-book",status="DELETED"}});
            });
            using var api=Client(fake);var original=await api.GetNotepadAsync("p1");
            try{await api.DeleteNotepadAsync(original);throw new Exception("An active book was reported deleted");}
            catch(MaiMemoException error){Check(error.Message.Contains("暂未确认")&&deletes==1&&reads==4,"Verification was unbounded or deletion was retried");}
        });
        await Test("Explicit DELETE rejections preserve their error without extra requests",async()=>
        {
            foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests,HttpStatusCode.InternalServerError})
            {
                var reads=0;var deletes=0;
                using var fake=new FakeHandler(request=>{if(request.Method==HttpMethod.Get){reads++;return Envelope(new {notepad=Pad("p1",content:"apple")});}deletes++;return new HttpResponseMessage(status);});
                using var api=Client(fake);var original=await api.GetNotepadAsync("p1");
                try{await api.DeleteNotepadAsync(original);throw new Exception("A rejected delete was acknowledged");}
                catch(MaiMemoException error){Check(error.StatusCode==status&&reads==2&&deletes==1,"An explicit rejection was masked or retried");}
            }
        });
        await Test("Caller cancellation is preserved without deletion verification or retry",async()=>
        {
            using var cancellation=new CancellationTokenSource();var reads=0;var deletes=0;
            using var fake=new FakeHandler((Func<HttpRequestMessage,HttpResponseMessage>)(request=>
            {
                if(request.Method==HttpMethod.Get){reads++;return Envelope(new {notepad=Pad("p1",content:"apple")});}
                deletes++;cancellation.Cancel();throw new OperationCanceledException(cancellation.Token);
            }));
            using var api=Client(fake);var original=await api.GetNotepadAsync("p1");
            try{await api.DeleteNotepadAsync(original,cancellation.Token);throw new Exception("Cancellation was ignored");}
            catch(OperationCanceledException){Check(reads==2&&deletes==1,"Cancellation caused a verification request or retry");}
        });
        await Test("Foreground activation shows hidden windows and releases attached input threads",()=>
        {
            var direct=new ActivationFake {AllowDirect=true};Check(new ForegroundWindow(direct).Activate((IntPtr)7,false)&&direct.Shows==1&&direct.Attachments.Count==0,"Direct activation failed");
            var fallback=new ActivationFake();Check(new ForegroundWindow(fallback).Activate((IntPtr)7,false)&&fallback.Attachments.SequenceEqual(new[]{true,false}),"Attached activation did not release its input link");return Task.CompletedTask;
        });
        await Test("Foreground fallback preserves the pin state and detaches even after failure",()=>
        {
            foreach(var pinned in new[]{false,true})
            {
                var native=new ActivationFake {Deny=true};new ForegroundWindow(native).Activate((IntPtr)7,pinned);
                Check(native.Pins.SequenceEqual(pinned?new[]{true}:new[]{true,false})&&native.Attachments.SequenceEqual(new[]{true,false}),"Foreground fallback changed the saved pin state");
            }
            var broken=new ActivationFake {ThrowOnBring=true};try{new ForegroundWindow(broken).Activate((IntPtr)7,false);throw new Exception("Expected injected failure");}catch(InvalidOperationException){}
            Check(broken.Attachments.SequenceEqual(new[]{true,false}),"Input threads remained linked after failure");return Task.CompletedTask;
        });
        await NewFeatureChecks.Run(Test,_folder);
        Console.WriteLine($"Backend tests: {_passed} passed, {_failed} failed. All HTTP responses were fake; the live token store was untouched.");
        return _failed == 0 ? 0 : 1;
    }

    private async Task Test(string name, Func<Task> action)
    {
        try { await action(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.Message.Replace(_token, "[redacted]")); }
    }

    private MaiMemoClient Client(HttpMessageHandler handler) => new(Tokens, handler, TimeSpan.Zero);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static object Pad(string id, string status = "PUBLISHED", string type = "NOTEPAD", string content = "") => new
    {
        id, title = "原来的词本", brief = "原来的介绍", tags = new[] { "考研" }, status, content, type,
        list = System.Array.Empty<object>()
    };
    private static HttpResponseMessage Envelope(object data) => Reply(JsonSerializer.Serialize(new { success = true, errors = System.Array.Empty<object>(), data }));
    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public void Dispose()
    {
        Tokens.DeleteToken();
        // The name and fully resolved target are controlled by this fixture.
        if (Directory.Exists(_folder) && Path.GetFileName(Path.GetFullPath(_folder)).StartsWith("MemoLex.BackendTests.", StringComparison.Ordinal))
            Directory.Delete(_folder, recursive: true);
    }
}

sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = request => Task.FromResult(respond(request));
    public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => _respond = respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _respond(request);
    }
}

sealed class StudyHandler : HttpMessageHandler
{
    internal bool InitiallyToday,IsNew=true,LoseAddResponse,HideAfterAdd,Rejected;
    internal int? StudyCount;
    internal string? FirstStudyDate,BadData;
    internal int AddedCount=1,Adds;
    internal readonly List<string> Paths=[];
    internal Action<HttpRequestMessage>? CheckRequest;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();CheckRequest?.Invoke(request);
        var path=request.RequestUri!.Segments.Last();Paths.Add(path);
        object value;
        switch(path)
        {
            case "get_today_items":
                if(BadData=="missing-today")value=new {};
                else value=new {today_items=InitiallyToday||(Adds>0&&AddedCount>0&&!HideAfterAdd)||BadData=="wrong-today"?new[]{new {voc_id=BadData=="wrong-today"?"different":"v1",voc_spelling="apple",is_new=IsNew,is_finished=false,order=1}}:[]};
                break;
            case "query_study_records":
                if(BadData=="missing-record")value=new {};
                else value=new {records=StudyCount.HasValue||BadData=="wrong-record"?new[]{new {voc_id=BadData=="wrong-record"?"different":"v1",voc_spelling="apple",study_count=StudyCount??0,first_study_date=FirstStudyDate}}:[],count=0};
                break;
            case "add_words":
                Adds++;
                if(LoseAddResponse)throw new HttpRequestException("Simulated response loss");
                if(Rejected)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                value=BadData=="bad-count"?new {added_count="invalid"}:(object)new {added_count=AddedCount};
                break;
            default:throw new Exception("Unexpected study request: "+path);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new {success=true,data=value}),Encoding.UTF8,"application/json")});
    }
}

sealed class ActivationFake : IForegroundWindowNative
{
    public bool AllowDirect,Deny,ThrowOnBring;public int Shows;private bool _attached;
    public List<bool> Attachments=new(),Pins=new();public IntPtr Foreground{get;private set;}=(IntPtr)9;public uint CurrentThread=>1;
    public uint WindowThread(IntPtr window)=>2;public void Show(IntPtr window)=>Shows++;
    public bool Activate(IntPtr window){if(!Deny&&(AllowDirect||_attached)){Foreground=window;return true;}return false;}
    public void BringToTop(IntPtr window){if(ThrowOnBring)throw new InvalidOperationException("injected");}
    public bool Attach(uint current,uint foreground,bool attach){Attachments.Add(attach);_attached=attach;return true;}
    public void Focus(IntPtr window){}public void Topmost(IntPtr window,bool value)=>Pins.Add(value);
}
