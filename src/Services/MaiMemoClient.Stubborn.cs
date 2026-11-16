using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace MemoLex.Services;

public sealed record StubbornWord(string Id,string Spelling,DateTimeOffset? NextStudyDate);
public sealed record StubbornWordList(IReadOnlyList<StubbornWord> Words,int Total,bool Complete);
public readonly record struct StubbornLoadProgress(int Loaded,int Total);

public sealed partial class MaiMemoClient
{
    public async Task<StubbornWordList> GetStubbornWordsAsync(IProgress<StubbornLoadProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        using var accountScope=BeginAccountOperation();
        const int limit=1000;
        var words=new Dictionary<string,StubbornWord>(StringComparer.Ordinal);
        var total=await CountAsync(null,null);
        progress?.Report(new(0,total));
        if(total==0)return new([],0,true);
        DateTimeOffset? start=null;
        var complete=true;
        for(var page=0;page<128&&words.Count<total;page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records=await PageAsync(start,null);
            foreach(var word in records)words[word.Id]=word;
            progress?.Report(new(words.Count,total));
            if(records.Count<limit)break;
            if(words.Count>=total)break;
            var boundary=records.Where(word=>word.NextStudyDate!=null).Select(word=>word.NextStudyDate!.Value).DefaultIfEmpty().Max();
            if(boundary==default){complete=false;break;}
            if(start is {} previous&&boundary<previous)
            {
                // Some servers round query timestamps to milliseconds.
                if(previous-boundary<TimeSpan.FromMilliseconds(1)){start=boundary.AddMilliseconds(1);continue;}
                complete=false;break;
            }
            // Include the whole boundary group before advancing; otherwise equal review dates can be lost.
            var boundaryCount=await CountAsync(boundary,boundary);
            if(boundaryCount>limit)complete=false;
            if(records.Count(word=>word.NextStudyDate==boundary)<Math.Min(boundaryCount,limit))
                foreach(var word in await PageAsync(boundary,boundary))words[word.Id]=word;
            progress?.Report(new(words.Count,total));
            var next=boundary.AddTicks(1);
            if(start is {} prior&&next<=prior){complete=false;break;}
            start=next;
        }
        if(words.Count<total)total=await CountAsync(null,null);
        return new(words.Values.OrderBy(word=>word.Spelling,StringComparer.OrdinalIgnoreCase).ToArray(),Math.Max(total,words.Count),complete&&words.Count>=total);

        object Body(bool count,DateTimeOffset? first,DateTimeOffset? last)
        {
            var body=new Dictionary<string,object>{{"tags","STICKING"},{"as_count",count},{"limit",limit}};
            if(first!=null||last!=null)
            {
                var dates=new Dictionary<string,string>();
                if(first!=null)dates["start"]=first.Value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
                if(last!=null)dates["end"]=last.Value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
                body["next_study_date"]=dates;
            }
            return body;
        }
        async Task<int> CountAsync(DateTimeOffset? first,DateTimeOffset? last)
        {
            var data=await SendAsync(HttpMethod.Post,"memo/study/query_study_records",Body(true,first,last),cancellationToken);
            if(!data.TryGetProperty("count",out var count)||count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out var value)||value<0)throw new MaiMemoException("暂时无法获取顽固单词数量，请稍后刷新。");
            return value;
        }
        async Task<IReadOnlyList<StubbornWord>> PageAsync(DateTimeOffset? first,DateTimeOffset? last)
        {
            var data=await SendAsync(HttpMethod.Post,"memo/study/query_study_records",Body(false,first,last),cancellationToken);
            if(!data.TryGetProperty("records",out var records)||records.ValueKind!=JsonValueKind.Array||records.GetArrayLength()>limit)throw new MaiMemoException("顽固单词数据不完整，请稍后刷新。");
            var output=new List<StubbornWord>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in records.EnumerateArray())
            {
                if(item.ValueKind!=JsonValueKind.Object)throw new MaiMemoException("顽固单词数据格式有变化，请稍后刷新。");
                var id=Text(item,"voc_id");var spelling=Text(item,"voc_spelling").Trim();
                var sticking=item.TryGetProperty("tags",out var tags)&&(tags.ValueKind==JsonValueKind.String&&tags.GetString()=="STICKING"||tags.ValueKind==JsonValueKind.Array&&tags.EnumerateArray().Any(tag=>tag.ValueKind==JsonValueKind.String&&tag.GetString()=="STICKING"));
                if(id.Length==0||spelling.Length is 0 or >100||spelling.Any(char.IsControl)||!ids.Add(id)||!sticking)throw new MaiMemoException("墨墨返回的顽固单词标记不完整，请先同步学习数据后刷新。");
                var date=DateTimeOffset.TryParse(Text(item,"next_study_date"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed)?parsed:(DateTimeOffset?)null;
                output.Add(new(id,spelling,date));
            }
            return output;
        }
    }
}
