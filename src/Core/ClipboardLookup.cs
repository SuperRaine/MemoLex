using System.Text.RegularExpressions;

namespace MemoLex.Core;

public enum ClipboardLookupKind { Ignore, Word, Chinese, Paragraph }
public sealed record ClipboardLookupRequest(ClipboardLookupKind Kind,string Text,string Direction="en|zh-CN");
public static class ClipboardLookup
{
    public static ClipboardLookupRequest Classify(string? raw,Func<string,bool> dictionaryContains)
    {
        var text=(raw??"").Trim();
        if(text.Length is 0 or >5000||text.Any(c=>char.IsControl(c)&&c is not '\r' and not '\n' and not '\t')
            ||Regex.IsMatch(text,@"^(?:https?://|file:|[A-Za-z]:\\)",RegexOptions.IgnoreCase)
            ||Regex.IsMatch(text,@"^[a-zA-Z0-9_+/=\-]{32,}$"))return new(ClipboardLookupKind.Ignore,text);
        var han=text.Count(c=>c is >= '\u3400' and <= '\u9fff');var latin=text.Count(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        if(han+latin==0)return new(ClipboardLookupKind.Ignore,text);
        var word=Regex.IsMatch(text,@"^[a-zA-Z][a-zA-Z’'\-]*$");
        if(word&&text.Length<=100||text.Length<=100&&!text.Contains('\n')&&dictionaryContains(text))return new(ClipboardLookupKind.Word,text);
        if(han>0&&text.Length<=12&&text.All(c=>c is >= '\u3400' and <= '\u9fff'||char.IsWhiteSpace(c)))return new(ClipboardLookupKind.Chinese,text);
        return new(ClipboardLookupKind.Paragraph,text,han>latin/2?"zh-CN|en":"en|zh-CN");
    }
}

public sealed record DoubleCopySignal(uint InitialSequence,IntPtr Foreground);
public sealed class DoubleCopyDetector
{
    private bool _cHeld;
    private long? _firstAt;
    private uint _sequence;
    private IntPtr _foreground;
    public void Reset(){_firstAt=null;_cHeld=false;}
    public DoubleCopySignal? Key(int key,bool down,bool control,bool otherModifier,bool injected,long milliseconds,uint sequence,IntPtr foreground)
    {
        if(injected)return null;
        if(key!=0x43){if(down&&key is not (0x11 or 0xA2 or 0xA3))_firstAt=null;return null;}
        if(!down){_cHeld=false;return null;}
        if(_cHeld)return null;
        _cHeld=true;
        if(!control||otherModifier||foreground==IntPtr.Zero){_firstAt=null;return null;}
        if(_firstAt is { } first&&milliseconds-first is >=0 and <=750&&foreground==_foreground)
        {var signal=new DoubleCopySignal(_sequence,foreground);_firstAt=null;return signal;}
        _firstAt=milliseconds;_sequence=sequence;_foreground=foreground;return null;
    }
}
