using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MemoLex.Core;

public sealed record StoredMnemonic(string Id,string NoteType,string Text,string? CloudId=null,string Origin="local",string? AccountKey=null);

public sealed class MnemonicStore(string? userPath=null,string? bundledPath=null)
{
    private readonly string _userPath=userPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoLex","mnemonics.user.json");
    private readonly string _bundledPath=bundledPath??Path.Combine(ApplicationPaths.RuntimeDirectory,"data","mnemonics.json");
    private Dictionary<string,StoredMnemonic[]> _bundled=new(StringComparer.OrdinalIgnoreCase),_saved=new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate=new();
    private bool _loaded,_writeBlocked;
    public string? LoadWarning {get;private set;}
    public int BundledCount=>_bundled.Count;
    public Task LoadAsync()=>Task.Run(()=>
    {
        lock(_gate)
        {
            if(_loaded)return;
            if(File.Exists(_bundledPath))_bundled=Read(_bundledPath,out _);
            var updatedTypes=false;
            try{if(File.Exists(_userPath))_saved=Read(_userPath,out updatedTypes);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {LoadWarning="本地个人助记文件读取失败，原文件已保留；暂时无法覆盖保存。";_writeBlocked=true;}
            if(!_writeBlocked)
            {
                if(updatedTypes)
                {
                    try{Write(_saved);}
                    catch(Exception error)when(error is IOException or UnauthorizedAccessException)
                    {LoadWarning="助记分类已更新，本地文件暂未保存。";}
                }
                MigrateGeneratedNotes();
            }
            _loaded=true;
        }
    });
    private sealed record RevisionManifest(int Schema,string BundleSha256,Dictionary<string,string[]> KnownGeneratedNotes);
    private void MigrateGeneratedNotes()
    {
        var path=Path.Combine(Path.GetDirectoryName(_bundledPath)!,"mnemonics.revisions.json");
        if(_saved.Count==0||!File.Exists(path))return;
        try
        {
            var manifest=JsonSerializer.Deserialize<RevisionManifest>(File.ReadAllText(path));
            if(manifest is not {Schema:1}||manifest.KnownGeneratedNotes is null)return;
            using(var stream=File.OpenRead(_bundledPath))
                if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(manifest.BundleSha256,StringComparison.OrdinalIgnoreCase))return;
            var known=new Dictionary<string,string[]>(manifest.KnownGeneratedNotes,StringComparer.OrdinalIgnoreCase);
            var next=new Dictionary<string,StoredMnemonic[]>(_saved,StringComparer.OrdinalIgnoreCase);
            var changed=false;var contentChanged=false;
            foreach(var (word,entries) in _saved)
            {
                if(!known.TryGetValue(word,out var hashes)||!_bundled.TryGetValue(word,out var bundled)||bundled.Length==0)continue;
                static bool Generated(StoredMnemonic note)=>note.CloudId is null&&note.AccountKey is null&&note.Origin is "local" or "builtin";
                StoredMnemonic? Current(StoredMnemonic note)=>Generated(note)?bundled.FirstOrDefault(item=>item.Id==note.Id&&item.Text==note.Text):null;
                bool Replaceable(StoredMnemonic note)=>Generated(note)&&(Current(note)!=null||hashes.Contains(
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(word.ToLowerInvariant()+"\0"+note.Id+"\0"+note.Text))),StringComparer.OrdinalIgnoreCase));
                var personalIds=entries.Where(note=>note.Origin!="cloud"&&!Replaceable(note)).Select(note=>note.Id).ToHashSet(StringComparer.Ordinal);
                var replacements=bundled.Where(note=>!personalIds.Contains(note.Id)).ToArray();
                // A bundle can retain an older note alongside a new one. Once both
                // are present, update their labels in place instead of appending
                // the newer note again at every launch. Preserve personal entries.
                if(replacements.All(item=>entries.Any(note=>Generated(note)&&note.Id==item.Id&&note.Text==item.Text)))
                {
                    var relabeled=entries.Select(note=>Current(note) is {} current?note with {NoteType=current.NoteType}:note).ToArray();
                    if(!entries.SequenceEqual(relabeled)){next[word]=relabeled;changed=true;}
                    continue;
                }
                var replaced=false;var updated=new List<StoredMnemonic>();
                foreach(var note in entries)
                {
                    if(Replaceable(note))
                    {if(!replaced)updated.AddRange(replacements);replaced=true;}
                    else updated.Add(note);
                }
                if(replaced&&!entries.SequenceEqual(updated))
                {
                    next[word]=updated.ToArray();changed=true;
                    if(!entries.Select(n=>n with {NoteType=""}).SequenceEqual(updated.Select(n=>n with {NoteType=""})))contentChanged=true;
                }
            }
            if(!changed)return;
            if(contentChanged)
            {
                var backup=_userPath+".before-bundled-update-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".bak";
                File.Copy(_userPath,backup,false);
            }
            Write(next);_saved=next;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {LoadWarning="新版助记暂未替换本地旧内容，原助记已保留。";}
    }
    private static Dictionary<string,StoredMnemonic[]> Read(string path,out bool updatedTypes)
    {
        var entries=JsonSerializer.Deserialize<Dictionary<string,StoredMnemonic[]>>(File.ReadAllText(path))??[];
        var result=entries.Where(item=>!string.IsNullOrWhiteSpace(item.Key)&&item.Value!=null)
            .ToDictionary(item=>item.Key,item=>item.Value.Where(note=>note!=null&&!string.IsNullOrWhiteSpace(note.Text)).Select(NormalizeType).ToArray(),StringComparer.OrdinalIgnoreCase);
        updatedTypes=entries.Any(item=>item.Value?.Any(note=>note!=null&&note.NoteType!=MnemonicTypes.Normalize(note.NoteType))==true);
        return result;
    }
    private static StoredMnemonic NormalizeType(StoredMnemonic note)=>note with {NoteType=MnemonicTypes.Normalize(note.NoteType)};
    private void Write(Dictionary<string,StoredMnemonic[]> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_userPath))!);
        var temporary=_userPath+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(entries));File.Move(temporary,_userPath,true);
    }
    public IReadOnlyList<StoredMnemonic> Get(string word)
    {
        lock(_gate)return (_saved.TryGetValue(word.Trim(),out var edited)?edited:_bundled.GetValueOrDefault(word.Trim())??[]).ToArray();
    }
    public IReadOnlyList<StoredMnemonic> Get(string word,string? accountKey)
    {
        lock(_gate)
        {
            if(_saved.TryGetValue(word.Trim(),out var saved))
            {
                var cloud=saved.Where(n=>n.Origin=="cloud"&&accountKey!=null&&n.AccountKey==accountKey).ToArray();
                if(cloud.Length>0)return cloud;
                var local=saved.Where(n=>n.Origin!="cloud").ToArray();if(local.Length>0)return local;
            }
            return (_bundled.GetValueOrDefault(word.Trim())??[]).ToArray();
        }
    }
    public void SaveCloud(string word,IEnumerable<StoredMnemonic> notes,string accountKey)
    {
        lock(_gate)
        {
            var existing=_saved.GetValueOrDefault(word.Trim())??[];
            Save(word,existing.Where(n=>n.Origin!="cloud"||n.AccountKey!=accountKey)
                .Concat(notes.Select(n=>n with {Origin="cloud",AccountKey=accountKey})));
        }
    }
    public void SaveEdit(string word,StoredMnemonic note,string? accountKey)
    {
        lock(_gate)
        {
            var existing=(_saved.GetValueOrDefault(word.Trim())??[]).ToList();
            existing.RemoveAll(n=>n.Id==note.Id&&(n.Origin!="cloud"||n.AccountKey==accountKey));
            existing.Add(note);Save(word,existing);
        }
    }
    public void Save(string word,IEnumerable<StoredMnemonic> notes)
    {
        word=word.Trim();if(word.Length is <1 or >100)throw new ArgumentException("请输入有效单词。");
        var records=notes.Select(NormalizeType).ToArray();if(records.Any(note=>string.IsNullOrWhiteSpace(note.Text)||note.Text.Length>20000))throw new ArgumentException("助记需为 1–20000 个字符。");
        lock(_gate)
        {
            if(!_loaded)throw new InvalidOperationException("请先读取本地助记，再保存修改。");
            if(_writeBlocked)throw new IOException(LoadWarning);
            if(_saved.TryGetValue(word,out var previous)&&previous.SequenceEqual(records))return;
            var next=new Dictionary<string,StoredMnemonic[]>(_saved,StringComparer.OrdinalIgnoreCase){[word]=records};
            Write(next);_saved=next;
        }
    }
}
