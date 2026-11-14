using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using MemoLex.Services;

var results=new List<object>();
using(var client=new GitHubUpdateClient())
{
    var watch=Stopwatch.StartNew();
    try{var latest=await client.GetLatestAsync(new Version(1,1,4));results.Add(new{mode="quick_no_update_check",milliseconds=watch.ElapsedMilliseconds,version=latest.Version.ToString(),ok=true});}
    catch(Exception error){results.Add(new{mode="quick_no_update_check",milliseconds=watch.ElapsedMilliseconds,ok=false,error=error.GetType().Name});}
}
using(var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(8)})
{
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex.UpdateProbe/1.0");
    var watch=Stopwatch.StartNew();
    try{using var request=new HttpRequestMessage(HttpMethod.Head,GitHubUpdateClient.Repository+"/releases/latest");using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);results.Add(new{mode="release_version_header",milliseconds=watch.ElapsedMilliseconds,status=(int)response.StatusCode,location=response.Headers.Location?.AbsolutePath,ok=true});}
    catch(Exception error){results.Add(new{mode="release_version_header",milliseconds=watch.ElapsedMilliseconds,ok=false,error=error.GetType().Name});}
}
Console.WriteLine(JsonSerializer.Serialize(results));
