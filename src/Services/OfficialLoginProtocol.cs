using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net;

namespace MemoLex.Services;

internal sealed record OfficialAuthorization(string Token,DateTimeOffset? ExpiresAt);
internal static partial class OfficialLoginProtocol
{
    internal const string StartUrl="https://open.maimemo.com/open/api/v1/tokens/openapi";
    internal static bool AllowedNavigation(string address)=>Uri.TryCreate(address,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0
        &&uri.Host is "accounts.maimemo.com" or "open.maimemo.com" or "www.maimemo.com";
    internal static bool IsTokenDocument(string address)=>Uri.TryCreate(address,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0
        &&uri.Host=="open.maimemo.com"&&uri.AbsolutePath=="/open/api/v1/tokens/openapi";
    internal static bool IsIdentitySource(string address)=>Uri.TryCreate(address,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0
        &&uri.Host=="accounts.maimemo.com"&&uri.AbsolutePath.StartsWith("/interaction/",StringComparison.Ordinal);
    internal static OfficialAuthorization? Parse(string address,string content,DateTimeOffset now)
    {
        if(!IsTokenDocument(address)||content.Length>65536)return null;
        var text=content.Trim();
        try
        {
            using var doc=JsonDocument.Parse(text);
            if(doc.RootElement.ValueKind==JsonValueKind.String)return Token(doc.RootElement.GetString()??"",null,now);
            return Find(doc.RootElement,now,0);
        }
        catch(JsonException)
        {
            if(TokenText().IsMatch(text))return Token(text,null,now);
            if(text.Contains('<'))return ParseHtml(text,now);
            // Only visible text from the official token document is accepted; never scripts, URLs, login form fields or request headers.
            var matches=VisibleToken().Matches(text).Select(m=>m.Value).Distinct().ToArray();
            return matches.Length==1&&text.Contains("token",StringComparison.OrdinalIgnoreCase)?Token(matches[0],null,now):null;
        }
    }
    private static OfficialAuthorization? ParseHtml(string text,DateTimeOffset now)
    {
        // The official token page masks the span with CSS. Read its literal value,
        // without revealing it in the UI, using the clipboard or executing page scripts.
        if(!text.Contains("tokenText",StringComparison.Ordinal)||!text.Contains("tokenBox",StringComparison.Ordinal))return null;
        var spans=TokenSpan().Matches(text);
        var literals=TokenLiteral().Matches(text);
        var values=spans.Select(m=>WebUtility.HtmlDecode(m.Groups["value"].Value).Trim())
            .Concat(literals.Select(m=>m.Groups["value"].Value)).Where(v=>TokenText().IsMatch(v)).Distinct(StringComparer.Ordinal).ToArray();
        if(values.Length!=1)return null;
        DateTimeOffset? expiry=null;
        var dates=ExpiryLiteral().Matches(text).Select(m=>m.Groups["value"].Value).Distinct().ToArray();
        if(dates.Length>1)return null;
        if(dates.Length==1)
        {
            if(!DateTimeOffset.TryParse(dates[0],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var parsed))return null;
            expiry=parsed;
        }
        else
        {
            var durations=DurationLiteral().Matches(text).Select(m=>m.Groups["value"].Value).Distinct().ToArray();
            if(durations.Length!=1||!int.TryParse(durations[0],out var seconds)||seconds is <=0 or >31536000)return null;
            expiry=now.AddSeconds(seconds);
        }
        return Token(values[0],expiry,now);
    }
    private static OfficialAuthorization? Find(JsonElement value,DateTimeOffset now,int depth)
    {
        if(depth>4||value.ValueKind!=JsonValueKind.Object)return null;
        DateTimeOffset? expiry=null;
        if(value.TryGetProperty("expires_at",out var at))
        {
            if(at.ValueKind==JsonValueKind.String&&DateTimeOffset.TryParse(at.GetString(),out var parsed))expiry=parsed;
            else if(at.ValueKind==JsonValueKind.Number&&at.TryGetInt64(out var timestamp)&&timestamp is >1000000000 and <100000000000)expiry=DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        if(expiry==null&&value.TryGetProperty("expires_in",out var seconds)&&seconds.ValueKind==JsonValueKind.Number&&seconds.TryGetInt32(out var duration)&&duration is >0 and <=31536000)expiry=now.AddSeconds(duration);
        foreach(var name in new[]{"openapi_token","api_token","access_token","token"})
            if(value.TryGetProperty(name,out var token)&&token.ValueKind==JsonValueKind.String&&Token(token.GetString()??"",expiry,now) is {} candidate)return candidate;
        foreach(var name in new[]{"data","result","openapi","token"})
            if(value.TryGetProperty(name,out var nested)&&Find(nested,now,depth+1) is {} result)
            {var effective=expiry??result.ExpiresAt;return effective<=now?null:result with {ExpiresAt=effective};}
        return null;
    }
    private static OfficialAuthorization? Token(string token,DateTimeOffset? expiry,DateTimeOffset now)
        =>TokenText().IsMatch(token)&&(expiry==null||expiry>now)?new(token,expiry??now.AddDays(7)):null;
    [GeneratedRegex(@"\A(?:[A-Za-z0-9_-]{24,256}|[A-Za-z0-9_-]{8,256}\.[A-Za-z0-9_-]{8,256}\.[A-Za-z0-9_-]{8,256})\z",RegexOptions.CultureInvariant)]
    private static partial Regex TokenText();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Fa-f0-9]{40,128}(?![A-Za-z0-9_-])",RegexOptions.CultureInvariant)]
    private static partial Regex VisibleToken();
    [GeneratedRegex("<span\\b(?=[^>]*\\bid\\s*=\\s*['\"]tokenText['\"])[^>]*>(?<value>[^<]{16,1024})</span\\s*>",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase)]
    private static partial Regex TokenSpan();
    [GeneratedRegex("\\b(?:var|let|const)\\s+token\\s*=\\s*(?<quote>['\"])(?<value>[A-Za-z0-9_.-]{24,770})\\k<quote>(?=\\s*(?:;|\\r?\\n))",RegexOptions.CultureInvariant)]
    private static partial Regex TokenLiteral();
    [GeneratedRegex("\\b(?:var|let|const)\\s+expiresAt\\s*=\\s*(?<quote>['\"])(?<value>[0-9TZtz:.+\\- ]{16,64})\\k<quote>(?=\\s*(?:;|\\r?\\n))",RegexOptions.CultureInvariant)]
    private static partial Regex ExpiryLiteral();
    [GeneratedRegex("\\b(?:var|let|const)\\s+expiresIn\\s*=\\s*(?<value>[0-9]{1,8})(?=\\s*(?:;|\\r?\\n))",RegexOptions.CultureInvariant)]
    private static partial Regex DurationLiteral();
}
