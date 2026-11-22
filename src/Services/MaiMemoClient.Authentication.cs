using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MemoLex.Services;

public sealed partial class MaiMemoClient
{
    // A single endpoint rejection may be transient. Confirm with an independent,
    // read-only vocabulary request before invalidating a saved account.
    private async Task RecheckAuthenticationAsync(string token,string credential,CancellationToken cancellation,bool retryRead)
    {
        var valid=await RecheckCredentialAsync(token,credential,cancellation);
        if(valid==true)
        {
            if(retryRead)return;
            throw new MaiMemoException("登录仍然有效，此次操作暂未完成，请稍后重试。");
        }
        throw new MaiMemoException("暂时无法确认墨墨登录状态，请检查网络后重试。");
    }
    private async Task<bool?> RecheckCredentialAsync(string token,string credential,CancellationToken cancellation)
    {
        var valid=await VerifyCredentialAsync(token,credential,cancellation);
        ValidateAccount(credential);
        if(valid is {} accepted)ReportAuthentication(token,accepted);
        if(valid==false)throw SafeHttpError(HttpStatusCode.Unauthorized);
        return valid;
    }
    private async Task<bool?> VerifyCredentialAsync(string token,string credential,CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if(_lastRequest!=0)
            {
                var remaining=_minimumRequestInterval-Stopwatch.GetElapsedTime(_lastRequest);
                if(remaining>TimeSpan.Zero)await Task.Delay(remaining,timeout.Token);
            }
            ValidateAccount(credential);
            using var request=new HttpRequestMessage(HttpMethod.Get,"vocabulary?spelling=hello");
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);_lastRequest=Stopwatch.GetTimestamp();
            using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            ValidateAccount(credential);
            if(response.StatusCode==HttpStatusCode.Unauthorized)return false;
            if((!response.IsSuccessStatusCode&&response.StatusCode!=HttpStatusCode.Forbidden)||response.Content.Headers.ContentLength>65536)return null;
            await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var bytes=new System.IO.MemoryStream();var buffer=new byte[4096];
            while(true){var count=await input.ReadAsync(buffer,timeout.Token);if(count==0)break;if(bytes.Length+count>65536)return null;bytes.Write(buffer,0,count);}
            using var json=JsonDocument.Parse(bytes.ToArray());var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return null;
            if(!response.IsSuccessStatusCode||root.TryGetProperty("success",out var success)&&success.ValueKind==JsonValueKind.False)
                return SafeApiError(root).StatusCode==HttpStatusCode.Unauthorized?false:null;
            if(root.TryGetProperty("data",out var data))root=data;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("voc",out var voc))return null;
            if(voc.ValueKind==JsonValueKind.Array)voc=voc.EnumerateArray().FirstOrDefault();
            return Text(voc,"id").Length>0?true:null;
        }
        catch(Exception error)when(!cancellation.IsCancellationRequested&&error is HttpRequestException or OperationCanceledException or JsonException or System.IO.IOException){return null;}
    }
}
