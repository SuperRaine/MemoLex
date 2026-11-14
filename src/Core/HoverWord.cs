using System.Text.RegularExpressions;

namespace MemoLex.Core;

public readonly record struct ScreenPoint(double X,double Y);
public readonly record struct ScreenBox(double X,double Y,double Width,double Height)
{
    public bool Contains(ScreenPoint point)=>Width>0&&Height>0&&point.X>=X&&point.X<X+Width&&point.Y>=Y&&point.Y<Y+Height;
}
public sealed record HoverSurface(ScreenBox Bounds,byte[] Samples)
{
    public ScreenPoint? TextPoint {get;init;}
    public string? Text {get;init;}
}
public sealed record HoverWord(string Word,ScreenBox Bounds,string Method,float Confidence=100)
{
    public HoverSurface? Surface {get;init;}
}
public sealed class HoverMotionGate
{
    private ScreenPoint? _anchor;
    private DateTimeOffset _since;
    private bool _armed,_consumed;
    public bool Observe(ScreenPoint point,DateTimeOffset now)
    {
        if(_anchor is not {} anchor){_anchor=point;_since=now;return false;}
        if(Math.Abs(point.X-anchor.X)<=3&&Math.Abs(point.Y-anchor.Y)<=3)return false;
        Arm(point,now);return true;
    }
    public void Arm(ScreenPoint point,DateTimeOffset now){_anchor=point;_since=now;_armed=true;_consumed=false;}
    public void Suppress(ScreenPoint? point=null){if(point!=null)_anchor=point;_armed=false;_consumed=true;}
    public void Reset(){_anchor=null;_armed=false;_consumed=false;}
    public bool TryBegin(DateTimeOffset now,int delayMs)
    {
        if(!_armed||_consumed||now-_since<TimeSpan.FromMilliseconds(Math.Clamp(delayMs,100,3000)))return false;
        _consumed=true;return true;
    }
}
public static class HoverSurfaces
{
    public static bool Same(HoverSurface? original,HoverSurface? current)
    {
        if(original==null||current==null||original.Bounds!=current.Bounds||original.Samples.Length!=current.Samples.Length||original.Samples.Length==0)return false;
        var changed=0;var total=0;
        for(var i=0;i<original.Samples.Length;i++){var difference=Math.Abs(original.Samples[i]-current.Samples[i]);total+=difference;if(difference>18)changed++;}
        return total<=original.Samples.Length*4&&changed<=original.Samples.Length*0.06;
    }
}
internal static partial class HoverWords
{
    internal sealed record AccessibleSpan(int Start,int Length,string? Word,bool IsChinese);
    internal static IReadOnlyList<AccessibleSpan> AccessibleSpans(string text)=>System.Text.RegularExpressions.Regex.Matches(text,@"[A-Za-zÀ-ÖØ-öø-ÿ]+(?:['’\-][A-Za-zÀ-ÖØ-öø-ÿ]+)*|[\u3400-\u9fff]+")
        .Select(match=>new AccessibleSpan(match.Index,match.Length,Normalize(match.Value),match.Value.Any(c=>c is >= '\u3400' and <= '\u9fff'))).ToArray();
    internal static HoverWord? AtPointer(IEnumerable<HoverWord> words,ScreenPoint point)
    {
        var candidates=words.Where(word=>IsReliableImageWord(word.Word,word.Confidence,word.Bounds)).ToArray();
        var exact=candidates.Where(word=>word.Bounds.Contains(point)).ToArray();
        if(exact.Length>0)return exact.Length==1?WithPointerMargin(exact[0]):null;
        // OCR bounds enclose the printed ink, while the user aims at the whole text line.
        // A small margin accepts descenders/baselines without jumping to a neighbouring word.
        var nearby=candidates.Where(word=>WithPointerMargin(word).Bounds.Contains(point)).ToArray();
        return nearby.Length==1?WithPointerMargin(nearby[0]):null;
    }
    private static HoverWord WithPointerMargin(HoverWord word)
    {
        var x=Math.Clamp(word.Bounds.Height*0.15,1,2);var y=Math.Clamp(word.Bounds.Height*0.35,2.5,4);
        return word with{Bounds=new(word.Bounds.X-x,word.Bounds.Y-y,word.Bounds.Width+2*x,word.Bounds.Height+2*y)};
    }
    internal static string? Normalize(string text)
    {
        text=text.Trim().Trim('"','“','”','‘','’','.',',',';',':','!','?','(',')','[',']');
        return text.Length<=48&&Word().IsMatch(text)?text.Replace('’','\''):null;
    }
    internal static bool IsReliableImageWord(string word,float confidence,ScreenBox bounds)
    {
        var letters=word.Count(char.IsLetter);
        var commonShort=word.ToLowerInvariant() is "to" or "of" or "in" or "on" or "at" or "as" or "is" or "be" or "we" or "he" or "it" or "by" or "no" or "so" or "do" or "if" or "up" or "us" or "an" or "or" or "my" or "me" or "go" or "am" or "ox";
        return confidence>=(letters==2?96:82)&&(letters>=3||letters==2&&commonShort)&&word.All(c=>char.IsAsciiLetter(c)||c is '\'' or '-')
            &&word.Where(char.IsLetter).Select(char.ToLowerInvariant).Distinct().Take(2).Count()>1
            &&bounds.Width>0&&bounds.Height>0&&bounds.Width/bounds.Height<=letters*0.95;
    }
    [GeneratedRegex(@"\A[A-Za-zÀ-ÖØ-öø-ÿ]+(?:['’\-][A-Za-zÀ-ÖØ-öø-ÿ]+)*\z",RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
