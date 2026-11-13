using System.IO;
using System.Text.Json;

namespace MemoLex.Core;

public sealed record StoredMnemonic(string Id,string NoteType,string Text,string? CloudId=null,string Origin="local");

public sealed class MnemonicStore(string? userPath=null,string? bundledPath=null)
{
    private readonly string _userPath=userPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoLex","mnemonics.user.json");
    private readonly string _bundledPath=bundledPath??Path.Combine(AppContext.BaseDirectory,"data","mnemonics.json");
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
            if(File.Exists(_bundledPath))_bundled=Read(_bundledPath);
            try{if(File.Exists(_userPath))_saved=Read(_userPath);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {LoadWarning="本地个人助记文件读取失败，原文件已保留；暂时无法覆盖保存。";_writeBlocked=true;}
            _loaded=true;
        }
    });
    private static Dictionary<string,StoredMnemonic[]> Read(string path)
    {
        var entries=JsonSerializer.Deserialize<Dictionary<string,StoredMnemonic[]>>(File.ReadAllText(path))??[];
        return entries.Where(item=>!string.IsNullOrWhiteSpace(item.Key)&&item.Value!=null)
            .ToDictionary(item=>item.Key,item=>item.Value.Where(note=>note!=null&&!string.IsNullOrWhiteSpace(note.Text)).ToArray(),StringComparer.OrdinalIgnoreCase);
    }
    public IReadOnlyList<StoredMnemonic> Get(string word)
    {
        lock(_gate)return (_saved.TryGetValue(word.Trim(),out var edited)?edited:_bundled.GetValueOrDefault(word.Trim())??[]).ToArray();
    }
    public void Save(string word,IEnumerable<StoredMnemonic> notes)
    {
        word=word.Trim();if(word.Length is <1 or >100)throw new ArgumentException("请输入有效单词。");
        var records=notes.ToArray();if(records.Any(note=>string.IsNullOrWhiteSpace(note.Text)||note.Text.Length>20000))throw new ArgumentException("助记需为 1–20000 个字符。");
        lock(_gate)
        {
            if(!_loaded)throw new InvalidOperationException("请先读取本地助记，再保存修改。");
            if(_writeBlocked)throw new IOException(LoadWarning);
            if(_saved.TryGetValue(word,out var previous)&&previous.SequenceEqual(records))return;
            var next=new Dictionary<string,StoredMnemonic[]>(_saved,StringComparer.OrdinalIgnoreCase){[word]=records};
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_userPath))!);
            var temporary=_userPath+".tmp";
            File.WriteAllText(temporary,JsonSerializer.Serialize(next));File.Move(temporary,_userPath,true);_saved=next;
        }
    }
}
