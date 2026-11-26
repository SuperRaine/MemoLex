using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MemoLex.Services;

public sealed record SavedAccount(string Id,string Name,string Identity,DateTimeOffset? ExpiresAt,bool NeedsLogin,bool Active)
{
    public override string ToString()=>Name+(NeedsLogin?" · 需重新登录":Active?" · 当前账号":"");
}

public sealed partial class TokenStore
{
    private readonly object _accountGate=new();
    private string? _memoryToken;
    private bool _memoryOnly;
    internal static TokenStore InMemory(string token)=>new TokenStore(""){_memoryOnly=true,_memoryToken=token};
    private sealed class AccountDocument
    {
        public AccountDocument(){}
        public int Version {get;set;}=2;
        public string? ActiveId {get;set;}
        public string? ExpiredId {get;set;}
        public List<AccountSecret> Accounts {get;set;}=[];
    }
    private sealed class AccountSecret
    {
        public AccountSecret(){}
        public string Id {get;set;}="";
        public string Name {get;set;}="";
        public string Identity {get;set;}="";
        public string? Token {get;set;}
        public DateTimeOffset? ExpiresAt {get;set;}
        public bool Expired {get;set;}
    }
    private AccountDocument ReadAccounts()
    {
        if(!File.Exists(_tokenPath))return new();
        var encrypted=File.ReadAllBytes(_tokenPath);
        if(encrypted.Length is <1 or >262144)throw new IOException("账号文件不可用，已保留原文件。");
        var plain=Transform(encrypted,false);
        try
        {
            var text=Encoding.UTF8.GetString(plain);
            if(!text.StartsWith('{'))
            {
                ValidateToken(text);var id="legacy-"+Fingerprint(text)![..16].ToLowerInvariant();
                return new(){ActiveId=id,Accounts=[new(){Id=id,Name="已保存的墨墨账号",Token=text}]};
            }
            var document=JsonSerializer.Deserialize<AccountDocument>(text)??throw new IOException("账号文件为空。");
            if(document.Version!=2||document.Accounts==null||document.Accounts.Count>30
                ||document.Accounts.Any(a=>a==null||string.IsNullOrWhiteSpace(a.Id)||a.Id.Length>64||a.Name==null||a.Identity==null||a.Name.Length>80||a.Identity.Length>200)
                ||document.Accounts.Select(a=>a.Id).Distinct().Count()!=document.Accounts.Count)
                throw new IOException("账号文件格式有误，已保留原文件。");
            return document;
        }
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    private void WriteAccounts(AccountDocument document)
    {
        if(_memoryOnly)throw new InvalidOperationException("临时验证账号不能保存。");
        var plain=JsonSerializer.SerializeToUtf8Bytes(document);byte[] encrypted;
        try{encrypted=Transform(plain,true);}finally{CryptographicOperations.ZeroMemory(plain);}
        var folder=Path.GetDirectoryName(Path.GetFullPath(_tokenPath))!;Directory.CreateDirectory(folder);
        var temporary=Path.Combine(folder,"account-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            File.WriteAllBytes(temporary,encrypted);
            if(File.Exists(_tokenPath))
            {
                var backups=Path.Combine(folder,"account-backups");Directory.CreateDirectory(backups);
                File.Copy(_tokenPath,Path.Combine(backups,DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N")+".dpapi"));
            }
            File.Move(temporary,_tokenPath,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static void ValidateToken(string token)
    {
        if(token.Length is <16 or >512||token.Any(char.IsWhiteSpace)||token.Any(char.IsControl))throw new ArgumentException("墨墨授权信息不完整，请重新登录。");
    }
    private static bool NeedsLogin(AccountSecret account)=>account.Expired||string.IsNullOrEmpty(account.Token)||account.ExpiresAt<=DateTimeOffset.UtcNow;
    private static SavedAccount Describe(AccountSecret account,string? active)=>new(account.Id,account.Name,account.Identity,account.ExpiresAt,NeedsLogin(account),account.Id==active&&!NeedsLogin(account));
    private string? ReadActiveToken()
    {
        if(_memoryOnly)return _memoryToken;
        lock(_accountGate)
        {
            try{var d=ReadAccounts();var a=d.Accounts.FirstOrDefault(a=>a.Id==d.ActiveId);return a!=null&&!NeedsLogin(a)?a.Token:null;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or CryptographicException or Win32Exception or JsonException or ArgumentException){return null;}
        }
    }
    public IReadOnlyList<SavedAccount> GetAccounts()
    {
        lock(_accountGate){var d=ReadAccounts();return d.Accounts.Select(a=>Describe(a,d.ActiveId)).ToArray();}
    }
    public SavedAccount? ActiveAccount {get{try{return GetAccounts().FirstOrDefault(a=>a.Active);}catch{return null;}}}
    internal SavedAccount? ExpiredAccount
    {
        get
        {
            try
            {
                lock (_accountGate)
                {
                    var document = ReadAccounts();
                    var id = document.ExpiredId ?? document.ActiveId;
                    var account = document.Accounts.FirstOrDefault(item => item.Id == id && NeedsLogin(item));
                    return account == null ? null : Describe(account, document.ActiveId);
                }
            }
            catch { return null; }
        }
    }
    public bool HasExpiredSession
    {
        get{try{lock(_accountGate){var d=ReadAccounts();return d.ExpiredId!=null||d.Accounts.Any(a=>a.Id==d.ActiveId&&NeedsLogin(a));}}catch{return false;}}
    }
    public SavedAccount SaveAccountToken(string token,string? identity,string? displayName,DateTimeOffset? expiresAt,string? targetId)
    {
        token=(token??"").Trim();ValidateToken(token);
        identity=(identity??"").Trim();displayName=(displayName??"").Trim();
        if(identity.Length>200||displayName.Length>80||expiresAt<=DateTimeOffset.UtcNow)throw new ArgumentException("账号名称或有效期无效。");
        lock(_accountGate)
        {
            var d=ReadAccounts();var fingerprint=Fingerprint(token);
            var sameToken=d.Accounts.FirstOrDefault(a=>a.Token!=null&&Fingerprint(a.Token)==fingerprint);
            var target=targetId==null?null:d.Accounts.FirstOrDefault(a=>a.Id==targetId)??throw new InvalidOperationException("原账号已移除，请使用添加账号重新登录。");
            if(target!=null&&identity.Length>0&&target.Identity.Length>0&&!target.Identity.Equals(identity,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("本次登录与所选账号不同，请使用“添加账号”；原账号未更改。");
            if(sameToken!=null&&(target!=null&&sameToken.Id!=target.Id||identity.Length>0&&sameToken.Identity.Length>0&&!sameToken.Identity.Equals(identity,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("本次授权属于其他已保存账号，已停止覆盖。请重新登录对应账号。");
            var a=target??(!string.IsNullOrEmpty(identity)?d.Accounts.FirstOrDefault(a=>a.Identity.Equals(identity,StringComparison.OrdinalIgnoreCase)):null)??sameToken;
            if(a==null)
            {
                if(d.Accounts.Count>=30)throw new InvalidOperationException("最多保存30个账号，请先移除不再使用的账号。");
                a=new(){Id=Guid.NewGuid().ToString("N")};d.Accounts.Add(a);
            }
            a.Identity=identity.Length>0?identity:a.Identity;
            if(displayName.Length>0)a.Name=displayName;
            else if(a.Name.Length==0)a.Name=identity.Length>0?MaskIdentity(identity):"墨墨账号 "+d.Accounts.Count;
            a.Token=token;a.Expired=false;a.ExpiresAt=expiresAt;d.ActiveId=a.Id;d.ExpiredId=null;WriteAccounts(d);
            return Describe(a,d.ActiveId);
        }
    }
    internal static string MaskIdentity(string identity)=>identity.Length>=7&&identity.All(c=>char.IsDigit(c)||c=='+')?identity[..3]+"****"+identity[^4..]:identity;
    public void SwitchAccount(string id)
    {
        lock(_accountGate)
        {
            var d=ReadAccounts();var a=d.Accounts.FirstOrDefault(a=>a.Id==id)??throw new ArgumentException("账号不存在。");
            if(NeedsLogin(a))throw new InvalidOperationException("这个账号需要重新登录。");
            d.ActiveId=id;d.ExpiredId=null;WriteAccounts(d);
        }
    }
    public void LogoutActive()
    {
        lock(_accountGate){var d=ReadAccounts();var a=d.Accounts.FirstOrDefault(a=>a.Id==d.ActiveId);if(a!=null){a.Token=null;a.ExpiresAt=null;}d.ActiveId=null;d.ExpiredId=null;WriteAccounts(d);}
    }
    public bool ExpireActive(string? expectedKey=null)
    {
        lock(_accountGate)
        {
            var d=ReadAccounts();var a=d.Accounts.FirstOrDefault(a=>a.Id==d.ActiveId);
            if(a==null||a.Token==null||expectedKey!=null&&Fingerprint(a.Token)!=expectedKey)return false;
            a.Token=null;a.Expired=true;d.ActiveId=null;d.ExpiredId=a.Id;WriteAccounts(d);return true;
        }
    }
    public bool ExpireIfDue()
    {
        lock(_accountGate){var d=ReadAccounts();var a=d.Accounts.FirstOrDefault(a=>a.Id==d.ActiveId);return a?.ExpiresAt<=DateTimeOffset.UtcNow&&ExpireActive();}
    }
    public void RemoveAccount(string id)
    {
        lock(_accountGate){var d=ReadAccounts();d.Accounts.RemoveAll(a=>a.Id==id);if(d.ActiveId==id)d.ActiveId=null;if(d.ExpiredId==id)d.ExpiredId=null;WriteAccounts(d);}
        StubbornWordCache.Remove(ProfileDirectory, id);
        FlashcardProgressStore.Remove(ProfileDirectory, id);
    }
    public void RenameAccount(string id,string name)
    {
        name=name.Trim();if(name.Length is <1 or >80)throw new ArgumentException("账号名称需为1至80字。");
        lock(_accountGate){var d=ReadAccounts();var a=d.Accounts.FirstOrDefault(a=>a.Id==id)??throw new ArgumentException("账号不存在。");a.Name=name;WriteAccounts(d);}
    }
}
