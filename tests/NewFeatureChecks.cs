using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using MemoLex.Services;

internal static class NewFeatureChecks
{
    internal static async Task Run(Func<string,Func<Task>,Task> test,string root)
    {
        void Check(bool value,string error){if(!value)throw new Exception(error);}
        string Token()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await test("Saved accounts reopen encrypted, switch independently and invalidate only the matching session",()=>
        {
            var path=Path.Combine(root,"accounts.dpapi");var store=new TokenStore(path);var first=Token();var second=Token();
            var a=store.SaveAccountToken(first,"13812345678",null,DateTimeOffset.UtcNow.AddDays(1),null);
            var b=store.SaveAccountToken(second,"13912345678","第二个账号",DateTimeOffset.UtcNow.AddDays(2),null);
            Check(store.GetToken()==second&&store.GetAccounts().Count==2,"Account save or selection failed");
            store=new TokenStore(path);store.SwitchAccount(a.Id);Check(store.GetToken()==first&&store.ActiveAccount?.Id==a.Id,"Account selection did not survive reopening");
            Check(!store.ExpireActive(TokenStore.Fingerprint(second))&&store.HasToken,"A stale account event logged out another account");
            Check(store.ExpireActive(TokenStore.Fingerprint(first))&&!store.HasToken&&store.HasExpiredSession,"Expiry did not remove the credential and log out");
            store.SwitchAccount(b.Id);Check(store.GetToken()==second&&!store.HasExpiredSession,"Another account was lost on expiry");
            store.RenameAccount(b.Id,"自定义名称");store.LogoutActive();Check(!store.HasToken&&store.GetAccounts().Count==2,"Logout removed the account list");
            var content=Encoding.UTF8.GetString(File.ReadAllBytes(path));Check(!content.Contains(first)&&!content.Contains(second)&&!content.Contains("13812345678"),"Account metadata or tokens were plaintext");
            return Task.CompletedTask;
        });
        await test("Legacy single-token DPAPI data migrates without losing the saved account",()=>
        {
            var secret=Token();var path=Path.Combine(root,"legacy.dpapi");
            var transform=typeof(TokenStore).GetMethod("Transform",BindingFlags.NonPublic|BindingFlags.Static)!;
            File.WriteAllBytes(path,(byte[])transform.Invoke(null,[Encoding.UTF8.GetBytes(secret),true])!);
            var store=new TokenStore(path);Check(store.GetToken()==secret&&store.GetAccounts().Count==1,"Legacy credential was not readable");
            var id=store.ActiveAccount!.Id;store.RenameAccount(id,"原来的账号");store=new TokenStore(path);
            Check(store.GetToken()==secret&&store.ActiveAccount?.Id==id&&store.ActiveAccount.Name=="原来的账号","Legacy migration lost data");
            return Task.CompletedTask;
        });
        await test("Relogin never overwrites another saved identity or transfers its credential",()=>
        {
            var path=Path.Combine(root,"separate-accounts.dpapi");var store=new TokenStore(path);var aToken=Token();var bToken=Token();
            var a=store.SaveAccountToken(aToken,"account-a","号1",null,null);var b=store.SaveAccountToken(bToken,"account-b","号2",null,null);var before=File.ReadAllBytes(path);
            foreach(var mode in new[]{"wrong-identity","wrong-token","new-wrong-token"})
            {
                try{store.SaveAccountToken(mode=="wrong-identity"?Token():bToken,mode=="wrong-identity"?"account-b":"account-a",null,null,mode=="new-wrong-token"?null:a.Id);throw new Exception("Different accounts were merged");}catch(InvalidOperationException){}
                Check(File.ReadAllBytes(path).SequenceEqual(before)&&store.GetAccounts().Count==2,"Rejected login modified the saved accounts");
            }
            var refreshed=Token();Check(store.SaveAccountToken(refreshed,"account-a",null,null,a.Id).Id==a.Id,"Valid relogin changed account identity");
            store.SwitchAccount(b.Id);Check(store.GetToken()==bToken,"Relogin replaced another account's token");
            Check(Directory.EnumerateFiles(Path.Combine(root,"account-backups"),"*.dpapi").Any(),"Encrypted recovery snapshots were not retained");
            return Task.CompletedTask;
        });
        await test("A multi-request study operation cannot continue under a different account",async()=>
        {
            var store=new TokenStore(Path.Combine(root,"operation-accounts.dpapi"));var aToken=Token();var bToken=Token();var a=store.SaveAccountToken(aToken,"a",null,null,null);var b=store.SaveAccountToken(bToken,"b",null,null,null);store.SwitchAccount(a.Id);var requests=0;
            using var handler=new FakeHandler(request=>
            {
                requests++;Check(request.Headers.Authorization?.Parameter==aToken,"A study operation crossed account credentials");store.SwitchAccount(b.Id);
                return Reply(new{success=true,data=new{today_items=Array.Empty<object>()}});
            });
            using var api=new MaiMemoClient(store,handler,TimeSpan.Zero);
            try{await api.AddToTodayAsync(new("a","talkative"));throw new Exception("Account switch did not cancel the old workflow");}catch(AccountChangedException){}
            Check(requests==1,"A switched workflow read or wrote another account");
        });
        await test("A UI-bound API rejects external active-account changes until the view is reloaded",async()=>
        {
            var store=new TokenStore(Path.Combine(root,"bound-accounts.dpapi"));var a=store.SaveAccountToken(Token(),"a",null,null,null);var aKey=store.CredentialKey;store.SaveAccountToken(Token(),"b",null,null,null);var requests=0;
            using var api=new MaiMemoClient(store,new FakeHandler(_=>{requests++;return Reply(new{success=true,data=new{records=new[]{new{voc_id="a",study_count=3}}}});}),TimeSpan.Zero){ExpectedCredentialKey=aKey};
            try{await api.GetTodayStudyStatusAsync(new("a","talkative"));throw new Exception("An old view read a new account");}catch(AccountChangedException){}
            Check(requests==0,"Mismatched view sent a request");api.ExpectedCredentialKey=store.CredentialKey;
            Check(await api.GetTodayStudyStatusAsync(new("a","talkative"))==TodayStudyStatus.AlreadyLearned&&requests==1,"Explicit account rebind failed");
        });
        await test("Corrupt credentials are preserved and known expiry removes only the expired token",async()=>
        {
            var path=Path.Combine(root,"corrupt.dpapi");File.WriteAllBytes(path,[1,2,3,4]);var store=new TokenStore(path);
            Check(!store.HasToken,"Invalid credential accepted");
            try{store.SaveToken(Token());throw new Exception("Corrupt credential overwritten");}catch(System.Security.Cryptography.CryptographicException){}
            Check(File.ReadAllBytes(path).SequenceEqual(new byte[]{1,2,3,4}),"Corrupt data was overwritten");
            store=new TokenStore(Path.Combine(root,"expiry.dpapi"));store.SaveAccountToken(Token(),"expire","",DateTimeOffset.UtcNow.AddSeconds(1),null);
            await Task.Delay(1200);Check(store.ExpireIfDue()&&!store.HasToken&&store.GetAccounts().Single().NeedsLogin,"Known expiry did not log out");
        });
        await test("Official login accepts tokens only from the exact official response and keeps expiry",()=>
        {
            var secret=Token();var now=DateTimeOffset.UtcNow;var json=JsonSerializer.Serialize(new {data=new {token=secret,expires_in=600}});
            var value=OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,json,now);
            Check(value?.Token==secret&&value.ExpiresAt==now.AddMinutes(10),"Official response did not retain token/expiry");
            foreach(var url in new[]{"https://evil.example/open/api/v1/tokens/openapi","http://open.maimemo.com/open/api/v1/tokens/openapi","https://open.maimemo.com.evil.example/open/api/v1/tokens/openapi","https://open.maimemo.com/open/api/v1/vocabulary"})Check(OfficialLoginProtocol.Parse(url,json,now)==null,"Untrusted page supplied a token");
            Check(OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,JsonSerializer.Serialize(new{token=secret,expires_at=now.AddDays(-1)}),now)==null,"Expired token accepted");
            return Task.CompletedTask;
        });
        await test("Live-shaped scheduled records with count one and unspecified response stay planned without writes",async()=>
        {
            var tokens=TokenStore.InMemory(Token());var writes=0;
            using var handler=new FakeHandler(request=>
            {
                var data=request.RequestUri!.AbsolutePath.EndsWith("get_today_items")?(object)new {today_items=new[]{new{voc_id="a",voc_spelling="apple",is_new=true,is_finished=false,first_response="STUDY_RESPONSE_UNSPECIFIED",order=1}}}:
                    new {records=new[]{new{voc_id="a",study_count=1,last_response="STUDY_RESPONSE_UNSPECIFIED",first_study_date="2026-11-13T16:00:00.000Z"}}};
                if(request.RequestUri.AbsolutePath.EndsWith("add_words"))writes++;
                return Reply(new{success=true,data});
            });
            using var api=new MaiMemoClient(tokens,handler,TimeSpan.Zero);
            Check(await api.GetTodayStudyStatusAsync(new("a","apple"))==TodayStudyStatus.AlreadyToday,"A scheduled word became learned");
            Check((await api.AddToTodayAsync(new("a","apple"))).Status==TodayStudyStatus.AlreadyToday&&writes==0,"A planned word was written again");
            Check((await api.GetTodayItemsAsync()).Single().Spelling=="apple","Today list parsing failed");
        });
        await test("Historical learning overrides a stale today-new flag in both lookup and add preflight",async()=>
        {
            foreach(var count in new[]{1,3})
            {
                var writes=0;var recordReads=0;
                using var handler=new FakeHandler(request=>
                {
                    var path=request.RequestUri!.AbsolutePath;
                    if(path.EndsWith("add_words")){writes++;throw new Exception("An already learned word reached add_words");}
                    if(path.EndsWith("query_study_records")){recordReads++;return Reply(new{success=true,data=new{records=new[]{new{voc_id="a",study_count=count,last_response=count==1?"FAMILIAR":"STUDY_RESPONSE_UNSPECIFIED"}}}});}
                    return Reply(new{success=true,data=new{today_items=new[]{new{voc_id="a",voc_spelling="talkative",is_new=true,is_finished=false}}}});
                });
                using var api=new MaiMemoClient(TokenStore.InMemory(Token()),handler,TimeSpan.Zero);
                Check(await api.GetTodayStudyStatusAsync(new("a","talkative"))==TodayStudyStatus.AlreadyLearned,"Lookup trusted a new flag over history");
                Check((await api.AddToTodayAsync(new("a","talkative"))).Status==TodayStudyStatus.AlreadyLearned&&writes==0&&recordReads==2,"Add preflight skipped historical records");
            }
        });
        await test("A count-one placeholder without today's new-word evidence requires sync and never adds",async()=>
        {
            foreach(var response in new[]{"STUDY_RESPONSE_UNSPECIFIED","","FUTURE_UNKNOWN_RESPONSE"})
            {
                var writes=0;
                using var handler=new FakeHandler(request=>
                {
                    var path=request.RequestUri!.AbsolutePath;if(path.EndsWith("add_words")){writes++;throw new Exception("Ambiguous records authorized adding");}
                    var data=path.EndsWith("get_today_items")?(object)new{today_items=Array.Empty<object>()}:new{records=new[]{new{voc_id="a",study_count=1,last_response=response,first_study_date="2026-11-13T16:00:00.000Z"}}};
                    return Reply(new{success=true,data});
                });
                using var api=new MaiMemoClient(TokenStore.InMemory(Token()),handler,TimeSpan.Zero);
                Check(await api.GetTodayStudyStatusAsync(new("a","talkative"))==TodayStudyStatus.SyncRequired,"A placeholder was called a new plan");
                Check((await api.AddToTodayAsync(new("a","talkative"))).Status==TodayStudyStatus.SyncRequired&&(await api.AddToTodayAsync(new("a","talkative"),true)).Status==TodayStudyStatus.SyncRequired&&writes==0,"Unknown status permitted a write");
            }
        });
        await test("An unfinished review item is learned even before today's first response is synced",async()=>
        {
            using var handler=new FakeHandler(request=>
            {
                var path=request.RequestUri!.AbsolutePath;if(path.EndsWith("add_words"))throw new Exception("A review word reached add_words");
                var data=path.EndsWith("get_today_items")?(object)new{today_items=new[]{new{voc_id="a",is_new=false,is_finished=false,first_response="STUDY_RESPONSE_UNSPECIFIED"}}}:new{records=new[]{new{voc_id="a",study_count=1,last_response="STUDY_RESPONSE_UNSPECIFIED"}}};
                return Reply(new{success=true,data});
            });
            using var api=new MaiMemoClient(TokenStore.InMemory(Token()),handler,TimeSpan.Zero);
            Check(await api.GetTodayStudyStatusAsync(new("a","talkative"))==TodayStudyStatus.AlreadyLearned&&(await api.AddToTodayAsync(new("a","talkative"))).Status==TodayStudyStatus.AlreadyLearned,"A review item became planned new learning");
        });
        await test("Synced feedback and familiar tags establish learning without requiring a second study day",async()=>
        {
            foreach(var mode in new[]{"feedback","prefixed-feedback","familiar-array","familiar-string"})
            {
                using var handler=new FakeHandler(request=>
                {
                    if(!request.RequestUri!.AbsolutePath.EndsWith("query_study_records"))throw new Exception("Known history should finish the lookup");
                    object tags=mode=="familiar-string"?"WELL_FAMILIAR":mode=="familiar-array"?new[]{"WELL_FAMILIAR"}:Array.Empty<string>();
                    return Reply(new{success=true,data=new{records=new[]{new{voc_id="a",study_count=mode=="feedback"?0:1,last_response=mode=="feedback"?"FORGET":mode=="prefixed-feedback"?"STUDY_RESPONSE_FAMILIAR":"STUDY_RESPONSE_UNSPECIFIED",tags}}}});
                });
                using var api=new MaiMemoClient(TokenStore.InMemory(Token()),handler,TimeSpan.Zero);
                Check(await api.GetTodayStudyStatusAsync(new("a","talkative"))==TodayStudyStatus.AlreadyLearned,"A genuine first response or familiar tag was ignored");
            }
        });
        await test("Post-add readback gives newly synced historical learning precedence over success",async()=>
        {
            var writes=0;
            using var handler=new FakeHandler(request=>
            {
                var path=request.RequestUri!.AbsolutePath;
                if(path.EndsWith("add_words")){writes++;return Reply(new{success=true,data=new{added_count=1}});}
                var data=path.EndsWith("query_study_records")?(object)new{records=writes==0?Array.Empty<object>():new object[]{new{voc_id="a",study_count=3,last_response="FAMILIAR"}}}:new{today_items=writes==0?Array.Empty<object>():new object[]{new{voc_id="a",is_new=true,is_finished=false}}};
                return Reply(new{success=true,data});
            });
            using var api=new MaiMemoClient(TokenStore.InMemory(Token()),handler,TimeSpan.Zero);
            Check((await api.AddToTodayAsync(new("a","talkative"))).Status==TodayStudyStatus.AlreadyLearned&&writes==1,"A stale new flag hid synchronized historical learning");
        });
        await test("An accepted addition with only placeholder readback stays uncertain without repeated writes",async()=>
        {
            var writes=0;
            using var handler=new FakeHandler(request=>
            {
                var path=request.RequestUri!.AbsolutePath;
                if(path.EndsWith("add_words")){writes++;return Reply(new{success=true,data=new{added_count=1}});}
                var data=path.EndsWith("query_study_records")?(object)new{records=writes==0?Array.Empty<object>():new object[]{new{voc_id="a",study_count=1,last_response="STUDY_RESPONSE_UNSPECIFIED"}}}:new{today_items=Array.Empty<object>()};
                return Reply(new{success=true,data});
            });
            using var api=new MaiMemoClient(TokenStore.InMemory(Token()),handler,TimeSpan.Zero);
            Check((await api.AddToTodayAsync(new("a","talkative"))).Status==TodayStudyStatus.SyncRequired&&(await api.AddToTodayAsync(new("a","talkative"),true)).Status==TodayStudyStatus.SyncRequired&&writes==1,"Ambiguous readback became planned new or retried a write");
        });
        await test("Official masked token HTML is read without revealing it and retains the server expiry",()=>
        {
            var secret=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
            var now=DateTimeOffset.UtcNow;var expiry=now.AddDays(1);
            var html=$"<html><div id='tokenBox'><span class='token-masked' id='tokenText'>••••••••</span></div><script>(function(){{\nvar token = '{secret}'\nvar expiresAt = '{expiry:O}'\nvar expiresIn = 86400\n}})()</script></html>";
            var parsed=OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,html,now);
            Check(parsed?.Token==secret&&parsed.ExpiresAt==expiry,"The masked HTML or exact expiry was lost");
            Check(OfficialLoginProtocol.Parse("https://accounts.maimemo.com/interaction/example",html,now)==null,"Login-form HTML supplied a token");
            Check(OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,html.Replace($"'{expiry:O}'",$"'{now.AddDays(-1):O}'"),now)==null,"Expired HTML authorization was accepted");
            Check(OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,html.Replace("••••••••",Token()),now)==null,"Conflicting token fields were accepted");
            return Task.CompletedTask;
        });
        await test("Unknown HTML cannot supply example script tokens and nested JSON rejects expired metadata",()=>
        {
            var secret=Token();var now=DateTimeOffset.UtcNow;
            Check(OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,$"<html><script>var token = '{secret}'\n</script></html>",now)==null,"An unrelated script token was accepted");
            var json=JsonSerializer.Serialize(new{expires_at=now.AddDays(-1),data=new{token=new{token=secret}}});
            Check(OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,json,now)==null,"Nested expired metadata was ignored");
            var valid=JsonSerializer.Serialize(new{data=new{token=new{token=secret,expires_in=3600}}});
            Check(OfficialLoginProtocol.Parse(OfficialLoginProtocol.StartUrl,valid,now)?.ExpiresAt==now.AddHours(1),"Nested token objects were not recognized");
            return Task.CompletedTask;
        });
        await test("Updater frequency can be disabled and semantic versions never use string ordering",()=>
        {
            var now=DateTimeOffset.UtcNow;
            Check(!GitHubUpdateClient.CheckDue(false,"daily",null,now,true),"Disabled auto update checked");
            Check(GitHubUpdateClient.CheckDue(true,"startup",now,now,true)&&!GitHubUpdateClient.CheckDue(true,"startup",null,now),"Startup frequency was periodic");
            Check(!GitHubUpdateClient.CheckDue(true,"daily",now.AddHours(-23),now)&&GitHubUpdateClient.CheckDue(true,"daily",now.AddDays(-1),now),"Daily interval wrong");
            Check(!GitHubUpdateClient.CheckDue(true,"weekly",now.AddDays(-6),now)&&GitHubUpdateClient.CheckDue(true,"6h",now.AddHours(-6),now),"Configured interval ignored");
            Check(GitHubUpdateClient.TryVersion("v1.10.0",out var v)&&v>new Version(1,9,9)&&!GitHubUpdateClient.TryVersion("v1.2.0-beta",out _),"Semantic version parsing failed");return Task.CompletedTask;
        });
        var bytes=Encoding.UTF8.GetBytes("verified update");var hash=Convert.ToHexString(SHA256.HashData(bytes));
        await test("A no-update check reads only the trusted latest-version redirect",async()=>
        {
            foreach(var installed in new[]{new Version(1,2,0),new Version(1,2,1)})
            {
                var requests=0;
                using var client=new GitHubUpdateClient(new FakeHandler(request=>
                {
                    requests++;Check(request.Method==HttpMethod.Head&&request.RequestUri!.AbsoluteUri==GitHubUpdateClient.Repository+"/releases/latest"&&request.Headers.Authorization==null,"No-update check loaded release bodies or package metadata");
                    return new(HttpStatusCode.Redirect){Headers={Location=new("/SuperRaine/MemoLex/releases/tag/v1.2.0",UriKind.Relative)}};
                }));
                var release=await client.GetLatestAsync(installed);
                Check(requests==1&&release.Version==new Version(1,2,0)&&release.Portable==null&&release.Installer==null,"No-update check fetched assets or changed the public version");
            }
        });
        await test("A blocked HEAD falls back to API version metadata without reading obsolete assets",async()=>
        {
            var requests=0;using var client=new GitHubUpdateClient(new FakeHandler(request=>
            {
                requests++;return request.Method==HttpMethod.Head?new(HttpStatusCode.MethodNotAllowed):Reply(new{tag_name="v1.1.0",draft=false,prerelease=false});
            }));
            var release=await client.GetLatestAsync(new Version(1,1,4));Check(requests==2&&release.Version==new Version(1,1,0)&&release.Portable==null,"Fallback required outdated attachment metadata");
        });
        await test("Version-only redirects reject foreign repositories, non-HTTPS and preview tags",async()=>
        {
            foreach(var location in new[]{"https://evil.example/SuperRaine/MemoLex/releases/tag/v1.2.0","http://github.com/SuperRaine/MemoLex/releases/tag/v1.2.0","https://github.com/other/repo/releases/tag/v1.2.0","https://github.com/SuperRaine/MemoLex/releases/tag/v1.2.0-beta","https://github.com/SuperRaine/MemoLex/releases/tag/v1.2.0?extra=1"})
            {
                using var client=new GitHubUpdateClient(new FakeHandler(_=>new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new(location)}}));
                try{await client.GetLatestAsync(new Version(1,2,0));throw new Exception("Untrusted version redirect accepted");}catch(UpdateException){}
            }
        });
        object Release(string url="https://github.com/SuperRaine/MemoLex/releases/download/v1.2.0/MemoLex-1.2.0-Windows-x64.zip",bool preview=false)=>new{tag_name="v1.2.0",draft=false,prerelease=preview,body="改进",assets=new[]{new{name="MemoLex-1.2.0-Windows-x64.zip",browser_download_url=url,size=bytes.Length,digest="sha256:"+hash}}};
        await test("Updater pins its repository and rejects previews and untrusted download addresses",async()=>
        {
            foreach(var mode in new[]{"valid","evil","preview"})
            {
                using var client=new GitHubUpdateClient(new FakeHandler(request=>{Check(request.Headers.Authorization==null,"Updater sent a credential");return Reply(Release(mode=="evil"?"https://evil.example/update.zip":"https://github.com/SuperRaine/MemoLex/releases/download/v1.2.0/MemoLex-1.2.0-Windows-x64.zip",mode=="preview"));}));
                try{var release=await client.GetLatestAsync();Check(mode=="valid"&&release.Portable?.Sha256==hash&&release.Version>new Version(1,1,2),"Bad release accepted");}catch(UpdateException){Check(mode!="valid","Valid release rejected");}
            }
        });
        await test("Updater verifies size and SHA256, cancels partial files and refuses HTTP redirects",async()=>
        {
            var asset=new UpdateAsset("MemoLex-1.2.0-Windows-x64.zip",new("https://github.com/SuperRaine/MemoLex/releases/download/v1.2.0/MemoLex-1.2.0-Windows-x64.zip"),bytes.Length,hash);
            foreach(var mode in new[]{"valid","corrupt","redirect"})
            {
                var folder=Path.Combine(root,"download-"+mode);
                using var client=new GitHubUpdateClient(new FakeHandler(_=>mode=="redirect"?new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new("http://evil.example/update.zip")}}:new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(mode=="corrupt"?new byte[bytes.Length]:bytes)}));
                try{var file=await client.DownloadAsync(asset,folder);Check(mode=="valid"&&await GitHubUpdateClient.ValidFileAsync(file,asset),"Invalid package accepted");}catch(UpdateException){Check(mode!="valid","Valid package rejected");}
                Check(!Directory.EnumerateFiles(folder,"*.part").Any(),"Incomplete download was left behind");
            }
            using var canceled=new CancellationTokenSource();canceled.Cancel();using var cancelClient=new GitHubUpdateClient(new FakeHandler((Func<HttpRequestMessage,HttpResponseMessage>)(_=>throw new Exception("Canceled update sent HTTP"))));
            try{await cancelClient.DownloadAsync(asset,Path.Combine(root,"cancel"),token:canceled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        });
        await test("GitHub REST rate limits fall back to the pinned latest release page with hash and exact size",async()=>
        {
            using var client=new GitHubUpdateClient(new FakeHandler(request=>
            {
                var uri=request.RequestUri!;
                if(uri.Host=="api.github.com")return new(HttpStatusCode.Forbidden);
                if(uri.AbsolutePath.EndsWith("/latest"))return new(HttpStatusCode.Redirect){Headers={Location=new("https://github.com/SuperRaine/MemoLex/releases/tag/v1.2.0")}};
                if(uri.AbsolutePath.Contains("/tag/"))return new(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent("official release")};
                if(uri.AbsolutePath.Contains("expanded_assets"))return new(HttpStatusCode.OK){Content=new StringContent("<li><a href=\"/SuperRaine/MemoLex/releases/download/v1.2.0/MemoLex-1.2.0-Windows-x64.zip\">package</a><clipboard-copy value=\"sha256:"+hash+"\"></clipboard-copy></li>")};
                Check(request.Method==HttpMethod.Head&&request.Headers.Authorization==null,"Fallback downloaded a package or sent credentials");
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
            }));
            var release=await client.GetLatestAsync();Check(release.Version==new Version(1,2,0)&&release.Portable?.Size==bytes.Length&&release.Portable.Sha256==hash,"Fallback lost release verification");
        });
        await test("A newer version still resolves verified assets after the quick header check",async()=>
        {
            var requests=0;using var client=new GitHubUpdateClient(new FakeHandler(request=>
            {
                requests++;return request.Method==HttpMethod.Head?new(HttpStatusCode.Redirect){Headers={Location=new(GitHubUpdateClient.Repository+"/releases/tag/v1.2.0")}}:Reply(Release());
            }));
            var release=await client.GetLatestAsync(new Version(1,1,4));Check(requests==2&&release.Portable?.Sha256==hash&&release.Portable.Size==bytes.Length,"Quick check bypassed update verification");
        });
        await test("Portable update extraction rejects traversal, duplicate paths and personal profiles before writing",()=>
        {
            foreach(var path in new[]{"MemoLex/../outside","MemoLex/token.dpapi","MemoLex/data/../../outside","MemoLex/C:bad","MemoLex/CON.txt","MemoLex/MemoLex.exe"})
            {
                var id=Guid.NewGuid().ToString("N");var archive=Path.Combine(root,id+".zip");var stage=Path.Combine(root,id);
                using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){Write(zip,"MemoLex/MemoLex.exe");Write(zip,"MemoLex/MemoLex.dll");Write(zip,path);}
                try{GitHubUpdateClient.ExtractPortable(archive,stage);throw new Exception("Unsafe path accepted");}catch(UpdateException){}
                Check(!Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories).Any(),"Unsafe archive partially extracted");
            }
            var valid=Path.Combine(root,"portable.zip");using(var zip=ZipFile.Open(valid,ZipArchiveMode.Create)){Write(zip,"MemoLex/MemoLex.exe");Write(zip,"MemoLex/MemoLex.dll");Write(zip,"MemoLex/data/offline.data");}
            var extracted=GitHubUpdateClient.ExtractPortable(valid,Path.Combine(root,"portable"));Check(extracted.Count==3&&File.Exists(Path.Combine(root,"portable/data/offline.data")),"Valid archive failed");return Task.CompletedTask;
        });
    }
    private static void Write(ZipArchive zip,string name){using var text=new StreamWriter(zip.CreateEntry(name).Open());text.Write("fixture");}
    private static HttpResponseMessage Reply(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
}
