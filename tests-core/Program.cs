using System.Text.Json;
using MemoLex.Core;

var root = Path.Combine(Path.GetTempPath(), "MemoLex.CoreTests." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
try
{
    void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    void Test(string name, Action<string> run)
    {
        var path = Path.Combine(root, passed + ".json");
        run(path); passed++; Console.WriteLine("PASS " + name);
    }
    Test("New and old profiles ask before applying a close action", path =>
    {
        Check(Preferences.Load(path).CloseToTray, "Missing profile must default to tray");
        Check(!Preferences.Load(path).RememberCloseAction,"New profiles must ask for a close action");
        File.WriteAllText(path, """{"NotepadId":"pad1","AlwaysOnTop":true,"History":["Word","WORD"," apple ",""]}""");
        var p = Preferences.Load(path);
        Check(p.DoubleCopyEnabled,"Old profiles did not enable double copy by default");
        Check(p.CloseToTray && p.NotepadId == "pad1" && p.AlwaysOnTop, "Existing settings were not preserved");
        Check(!p.RememberCloseAction,"Old profiles silently skipped the first close prompt");
        Check(p.SearchHistory.Select(x => x.Word).SequenceEqual(new[] { "Word", "apple" }), "Legacy words not migrated or deduplicated");
        Check(p.SearchHistory.All(x => x.LastSearched == null), "Migration must not invent timestamps");
        p.Save();
        Check(Preferences.Load(path).SearchHistory.Count == 2, "Migration not durable");
    });
    Test("A remembered close action survives restart and can be reset to ask again",path=>
    {
        var p=Preferences.Load(path);p.CloseToTray=false;p.RememberCloseAction=true;p.Save();
        p=Preferences.Load(path);Check(p.RememberCloseAction&&!p.CloseToTray,"Remembered exit action was lost");
        p.CloseToTray=true;p.Save();p=Preferences.Load(path);Check(p.RememberCloseAction&&p.CloseToTray,"Remembered tray action was lost");
        p.RememberCloseAction=false;p.Save();Check(!Preferences.Load(path).RememberCloseAction,"Ask-again setting was not saved");
    });
    Test("Direct-exit option survives restart and can be switched back", path =>
    {
        var p = Preferences.Load(path); p.CloseToTray = false;p.DoubleCopyEnabled=false; p.Save();
        p = Preferences.Load(path); Check(!p.CloseToTray&&!p.DoubleCopyEnabled, "False was replaced by the default");
        p.CloseToTray = true; p.Save(); Check(Preferences.Load(path).CloseToTray, "Tray preference not saved");
    });
    Test("User-confirmed learned words persist only for the explicitly selected account",path=>
    {
        var p=Preferences.Load(path);p.ConfirmLearned("account-a","talkative");p.ConfirmLearned("account-a","TALKATIVE");p.Save();p=Preferences.Load(path);
        Check(p.IsConfirmedLearned("account-a","Talkative")&&p.ConfirmedLearnedWords["account-a"].Count==1,"The explicit confirmation did not persist or deduplicate");
        Check(!p.IsConfirmedLearned("account-b","talkative")&&!p.IsConfirmedLearned(null,"talkative")&&!p.IsConfirmedLearned("account-a","reader"),"Learning crossed accounts, logged out state or words");
    });
    Test("Repeated lookup updates time, count and recency without duplicates", path =>
    {
        var p = Preferences.Load(path);
        var first = DateTimeOffset.Parse("2026-11-13T10:00:00+08:00");
        p.RecordLookup("apple", first); p.RecordLookup("talkative", first.AddMinutes(1)); p.RecordLookup("APPLE", first.AddMinutes(2)); p.Save();
        p = Preferences.Load(path);
        Check(p.SearchHistory.Count == 2 && p.SearchHistory[0].Word == "APPLE", "Most recent lookup not first");
        Check(p.SearchHistory[0].LookupCount == 2 && p.SearchHistory[0].LastSearched == first.AddMinutes(2), "Count/time not preserved");
        Check(p.SearchHistory[1].Word == "talkative", "Unrelated entry lost");
    });
    Test("The 500-entry limit retains the newest words", path =>
    {
        var p = Preferences.Load(path);
        for (var i = 0; i < 1001; i++) p.RecordLookup("word" + i);
        p.Save(); p = Preferences.Load(path);
        Check(p.SearchHistory.Count == 500, "Unexpected history size");
        Check(p.SearchHistory[0].Word == "word1000" && p.SearchHistory[^1].Word == "word501", "Wrong end of history was pruned");
    });
    Test("Deleting one entry is durable and ignores case", path =>
    {
        var p = Preferences.Load(path); p.RecordLookup("apple"); p.RecordLookup("talkative"); p.RemoveLookup("APPLE"); p.Save();
        p = Preferences.Load(path);
        Check(p.SearchHistory.Count == 1 && p.SearchHistory[0].Word == "talkative", "Delete did not preserve the other word");
    });
    Test("Clearing history does not resurrect the legacy list on restart", path =>
    {
        File.WriteAllText(path, """{"History":["apple","talkative"]}""");
        var p = Preferences.Load(path); p.ClearHistory(); p.Save(); p = Preferences.Load(path);
        Check(p.SearchHistory.Count == 0 && p.History.Count == 0, "Cleared history came back");
    });
    Test("Null legacy fields and invalid words do not crash migration", path =>
    {
        File.WriteAllText(path, """{"History":null,"SearchHistory":[null,{"Word":" "},{"Word":"apple","LookupCount":0}]}""");
        var p = Preferences.Load(path);
        Check(p.SearchHistory.Count == 1 && p.SearchHistory[0].LookupCount == 1, "Invalid history normalization failed");
    });
    Test("Broken JSON can still start with empty history", path =>
    {
        File.WriteAllText(path, "{broken");
        var p = Preferences.Load(path);
        Check(p.CloseToTray && p.SearchHistory.Count == 0, "Corrupt profile prevented defaults");
    });
    NavigationJournal<NavState> Journal(int capacity = 120) => new((a,b) => a.Word.Equals(b.Word,StringComparison.OrdinalIgnoreCase) && a.Page == b.Page && a.Filter == b.Filter,capacity);
    Test("Back and forward visit every query in order", _ =>
    {
        var j=Journal();j.Navigate(new("apple"));j.Navigate(new("reader"));j.Navigate(new("learning"));
        Check(j.GoBack()?.Word=="reader" && j.GoBack()?.Word=="apple" && j.GoBack()==null,"Back order wrong");
        Check(j.GoForward()?.Word=="reader" && j.GoForward()?.Word=="learning" && j.GoForward()==null,"Forward order wrong");
    });
    Test("Updating scroll and pagination preserves a forward branch", _ =>
    {
        var j=Journal();j.Navigate(new("apple"));j.Navigate(new("reader"));j.GoBack();
        j.UpdateCurrent(new("apple",Scroll:420,Loaded:20));
        Check(j.Count==2 && j.CanGoForward && j.Current?.Loaded==20 && j.Current?.Scroll==420,"Updating view altered the journal");
        j.GoForward();Check(j.GoBack()?.Scroll==420,"Scroll snapshot lost");
    });
    Test("Same-location lookups update without duplicate navigation", _ =>
    {
        var j=Journal();j.Navigate(new("apple"));j.Navigate(new("reader"));j.GoBack();j.Navigate(new("APPLE",Scroll:12));
        Check(j.Count==2 && j.CanGoForward && j.Current?.Scroll==12,"Same location duplicated or destroyed forward history");
    });
    Test("New navigation after going back clears only the forward branch", _ =>
    {
        var j=Journal();j.Navigate(new("apple"));j.Navigate(new("reader"));j.Navigate(new("learning"));j.GoBack();j.Navigate(new("novel"));
        Check(!j.CanGoForward && j.GoBack()?.Word=="reader" && j.GoBack()?.Word=="apple","Branch behavior wrong");
        j.GoForward();Check(j.GoForward()?.Word=="novel","Obsolete forward page was resurrected");
    });
    Test("Sections and exam filters are independently navigable", _ =>
    {
        var j=Journal();j.Navigate(new("apple"));j.Navigate(new("apple",Page:2));j.Navigate(new("apple",Page:2,Filter:"CET6"));
        Check(j.GoBack()?.Filter=="ALL" && j.Current?.Page==2 && j.GoBack()?.Page==0,"View and filter locations collapsed");
    });
    Test("The bounded journal retains its newest destinations", _ =>
    {
        var j=Journal(3);foreach(var word in new[]{"one","two","three","four"})j.Navigate(new(word));
        Check(j.Count==3 && j.GoBack()?.Word=="three" && j.GoBack()?.Word=="two" && !j.CanGoBack,"Wrong history entries pruned");
    });
    Test("Chinese queries and English lookups coexist in persistent history",path=>
    {
        var p=Preferences.Load(path);p.RecordLookup("快乐");p.RecordLookup("happy");p.RecordLookup("管理");p.RecordLookup("快乐");p.Save();
        var loaded=Preferences.Load(path);Check(loaded.SearchHistory.Select(item=>item.Word).SequenceEqual(new[]{"快乐","管理","happy"})&&loaded.SearchHistory[0].LookupCount==2,"Chinese query history did not round trip");
    });
    void MigrationCase(string name,Action<string,string,StoredMnemonic,StoredMnemonic> run)
    {
        Test(name,path=>
        {
            var folder=path+".data";Directory.CreateDirectory(folder);var bundle=Path.Combine(folder,"mnemonics.json");
            var old=new StoredMnemonic("personal:old","词根词缀","student：学生。-ent（人）。");
            var fresh=new StoredMnemonic("builtin:reviewed","词根词缀","stud（学习）＋-ent（人）→学习的人→学生。",Origin:"builtin");
            File.WriteAllText(bundle,JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[fresh]}}));
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("student\0"+old.Id+"\0"+old.Text))).ToLowerInvariant();
            var bundleHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(bundle))).ToLowerInvariant();
            File.WriteAllText(Path.Combine(folder,"mnemonics.revisions.json"),JsonSerializer.Serialize(new {Schema=1,BundleSha256=bundleHash,KnownGeneratedNotes=new Dictionary<string,string[]>{{"student",[hash]}}}));
            run(path,bundle,old,fresh);
        });
    }
    MigrationCase("Unchanged generated notes update durably with an exact original backup",(path,bundle,old,fresh)=>
    {
        var content=JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[old]}});File.WriteAllText(path,content);
        var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(store.Get("student").SequenceEqual(new[]{fresh}),"Generated note did not migrate");
        var backups=Directory.GetFiles(root,Path.GetFileName(path)+".before-bundled-update-*.bak");Check(backups.Length==1&&File.ReadAllText(backups[0])==content,"Original backup is not byte-identical");
        var restarted=new MnemonicStore(path,bundle);restarted.LoadAsync().GetAwaiter().GetResult();Check(restarted.Get("student")[0]==fresh&&Directory.GetFiles(root,Path.GetFileName(path)+".before-bundled-update-*.bak").Length==1,"Migration repeated or failed after restart");
    });
    MigrationCase("An edited note with the same generated identifier remains unchanged",(path,bundle,old,fresh)=>
    {
        var edited=old with {Text="我自己改写的学生助记"};File.WriteAllText(path,JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[edited]}}));
        var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(store.Get("student")[0]==edited,"User edit was replaced");
    });
    MigrationCase("Cloud notes from both accounts survive while local generated content updates",(path,bundle,old,fresh)=>
    {
        var a=old with {Origin="cloud",CloudId="a",AccountKey="account-a"};var b=old with {Origin="cloud",CloudId="b",AccountKey="account-b"};
        File.WriteAllText(path,JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[old,a,b]}}));
        var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(store.Get("student","account-a").SequenceEqual(new[]{a})&&store.Get("student","account-b").SequenceEqual(new[]{b}),"Cloud priority or account isolation changed");
        Check(store.Get("student",null).SequenceEqual(new[]{fresh}),"Local note did not update");
    });
    MigrationCase("A matching text under a custom identifier is a protected user note",(path,bundle,old,fresh)=>
    {
        var custom=old with {Id="local:user-created"};File.WriteAllText(path,JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[custom]}}));
        var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(store.Get("student")[0]==custom,"Text-only match overwrote a custom note");
    });
    MigrationCase("A stale manifest cannot migrate a different bundled dictionary",(path,bundle,old,fresh)=>
    {
        File.WriteAllText(path,JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[old]}}));File.AppendAllText(bundle," ");
        var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(store.Get("student")[0]==old,"Bundle checksum mismatch was ignored");
    });
    MigrationCase("Broken personal JSON is preserved and blocks saving",(path,bundle,old,fresh)=>
    {
        File.WriteAllText(path,"{broken");var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(File.ReadAllText(path)=="{broken"&&store.LoadWarning!=null,"Broken profile changed");
        try{store.Save("student",[fresh]);throw new Exception("Save was allowed");}catch(IOException){}
    });
    MigrationCase("Mixed edited and unrelated notes are preserved alongside the updated generated note",(path,bundle,old,fresh)=>
    {
        var custom=old with {Id="custom",Text="自写内容"};var other=old with {Id="other",Text="其他词"};
        File.WriteAllText(path,JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[old,custom]},{"other",[other]}}));
        var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(store.Get("student").SequenceEqual(new[]{fresh,custom})&&store.Get("other")[0]==other,"Unrelated or custom content was lost");
    });
    MigrationCase("Malformed revision metadata leaves saved content untouched",(path,bundle,old,fresh)=>
    {
        var content=JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"student",[old]}});File.WriteAllText(path,content);File.WriteAllText(Path.Combine(Path.GetDirectoryName(bundle)!,"mnemonics.revisions.json"),"{broken");
        var store=new MnemonicStore(path,bundle);store.LoadAsync().GetAwaiter().GetResult();Check(store.Get("student")[0]==old&&File.ReadAllText(path)==content,"Malformed manifest changed user data");
    });
    Console.WriteLine($"{passed} core behavior checks passed; all data used an isolated temporary profile.");
    return 0;
}
finally
{
    if(Path.GetDirectoryName(Path.GetFullPath(root))!.TrimEnd(Path.DirectorySeparatorChar)!=Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
        ||!Path.GetFileName(root).StartsWith("MemoLex.CoreTests.",StringComparison.Ordinal))throw new Exception("Unsafe test cleanup path");
    Directory.Delete(root,true);
}
sealed record NavState(string Word,int Page=0,string Filter="ALL",double Scroll=0,int Loaded=10);
