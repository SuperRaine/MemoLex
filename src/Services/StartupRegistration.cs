using System.IO;
using Microsoft.Win32;

namespace MemoLex.Services;

internal interface IStartupRegistration
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
}

internal sealed class StartupRegistration : IStartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "MemoLex";
    private readonly string _executable;
    private readonly string _keyPath;
    internal StartupRegistration(string? executable=null,string? keyPath=null)
    {
        _executable=Path.GetFullPath(executable??Path.Combine(AppContext.BaseDirectory,"MemoLex.exe"));
        _keyPath=keyPath??RunKey;
    }
    public bool Enabled
    {
        get
        {
            using var key=Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(ValueName) is string command&&!string.IsNullOrWhiteSpace(command);
        }
    }
    public void SetEnabled(bool enabled)
    {
        if(enabled)
        {
            if(!File.Exists(_executable))throw new IOException("找不到墨墨搜词程序，请从安装目录打开后重试。");
            using var key=Registry.CurrentUser.CreateSubKey(_keyPath,true);
            key.SetValue(ValueName,'"'+_executable+'"',RegistryValueKind.String);
        }
        else
        {
            using var key=Registry.CurrentUser.OpenSubKey(_keyPath,true);
            key?.DeleteValue(ValueName,false);
        }
    }
}
