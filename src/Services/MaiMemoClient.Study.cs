using System.Net.Http;
using System.Text.Json;

namespace MemoLex.Services;

public enum TodayStudyStatus { Added, AlreadyToday, AlreadyLearned, NotAdded, PendingVerification, Planned, SyncRequired }
public sealed record TodayStudyResult(TodayStudyStatus Status,int? AddedCount=null);
public sealed record TodayStudyItem(string Id,string Spelling,bool IsNew,bool IsFinished,bool Responded,int Order)
{
    public string Kind => IsNew ? "新学" : "复习";
}
internal sealed class StudyAddUncertainException(string message) : MaiMemoException(message);

public sealed partial class MaiMemoClient
{
    private enum StudyRecordState { Absent, Unstudied, Learned, Uncertain }
    /// <summary>Reads learning status without adding a word or advancing review.</summary>
    public async Task<TodayStudyStatus> GetTodayStudyStatusAsync(Vocabulary word,CancellationToken cancellationToken=default)
    {
        using var accountScope=BeginAccountOperation();
        ArgumentNullException.ThrowIfNull(word);
        if(string.IsNullOrWhiteSpace(word.Id)||word.Id.Length>200||word.Id.Any(char.IsControl))throw new ArgumentException("无效的墨墨单词 ID。");
        var studied=await ReadStudiedWordAsync(word.Id,cancellationToken);
        if(studied==StudyRecordState.Learned)return TodayStudyStatus.AlreadyLearned;
        var today=await ReadTodayWordAsync(word.Id,cancellationToken);
        return Classify(today,studied);
    }
    private static TodayStudyStatus Classify(TodayStudyItem? today,StudyRecordState studied)
    {
        if(studied==StudyRecordState.Learned||today is {IsNew:false} or {IsFinished:true} or {Responded:true})return TodayStudyStatus.AlreadyLearned;
        if(today is {IsNew:true})return TodayStudyStatus.AlreadyToday;
        return studied switch{StudyRecordState.Unstudied=>TodayStudyStatus.Planned,StudyRecordState.Absent=>TodayStudyStatus.NotAdded,_=>TodayStudyStatus.SyncRequired};
    }

    public Task<IReadOnlyList<TodayStudyItem>> GetTodayItemsAsync(CancellationToken cancellationToken=default) =>
        GetTodayItemsAsync(isFinished: null, isNew: null, cancellationToken);

    public async Task<IReadOnlyList<TodayStudyItem>> GetTodayItemsAsync(bool? isFinished,bool? isNew,CancellationToken cancellationToken=default)
    {
        using var accountScope=BeginAccountOperation();
        var body=new Dictionary<string,object>{{"limit",StudyBatchLimit}};
        if(isFinished.HasValue)body["is_finished"]=isFinished.Value;
        if(isNew.HasValue)body["is_new"]=isNew.Value;
        var data=await SendAsync(HttpMethod.Post,"memo/study/get_today_items",body,cancellationToken);
        return ParseToday(data,20000).OrderBy(item=>item.Order).ToArray();
    }

    /// <summary>Adds an unstudied word, advances it to today and verifies the server's new-word flag.</summary>
    public async Task<TodayStudyResult> AddToTodayAsync(Vocabulary word,bool verifyOnly=false,CancellationToken cancellationToken=default)
    {
        using var accountScope=BeginAccountOperation();
        ArgumentNullException.ThrowIfNull(word);
        if(string.IsNullOrWhiteSpace(word.Id)||word.Id.Length>200||word.Id.Any(char.IsControl))throw new ArgumentException("无效的墨墨单词 ID。");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var today=await ReadTodayWordAsync(word.Id,cancellationToken);
            // Historical learning always takes precedence over today's possibly stale new-word flag.
            var record=await ReadStudiedWordAsync(word.Id,cancellationToken);
            var current=Classify(today,record);
            if(current!=TodayStudyStatus.NotAdded)return new(current);
            if(verifyOnly)return new(TodayStudyStatus.NotAdded);

            int? added=null;
            try
            {
                var data=await SendAsync(HttpMethod.Post,"memo/study/add_words",new {words=new[]{new {id=word.Id}},advance=true},cancellationToken);
                if(!data.TryGetProperty("added_count",out var count)||count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out var value)||value is <0 or >1)
                    throw new StudyAddUncertainException("墨墨没有返回有效的添加数量，请核对今日新学。");
                added=value;
            }
            catch(StudyAddUncertainException) { /* Verify once. Never repeat an uncertain write. */ }

            try
            {
                today=await ReadTodayWordAsync(word.Id,cancellationToken);
                record=await ReadStudiedWordAsync(word.Id,cancellationToken);
                if(today!=null||record!=StudyRecordState.Absent)
                {
                    var state=Classify(today,record);
                    return new(state==TodayStudyStatus.AlreadyToday&&added==1?TodayStudyStatus.Added:state,added);
                }
            }
            catch(MaiMemoException) { return new(TodayStudyStatus.PendingVerification,added); }
            return new(added==0?TodayStudyStatus.NotAdded:TodayStudyStatus.PendingVerification,added);
        }
        finally{_writeGate.Release();}
    }

    private async Task<TodayStudyItem?> ReadTodayWordAsync(string id,CancellationToken cancellationToken)
    {
        var data=await SendAsync(HttpMethod.Post,"memo/study/get_today_items",new {voc_ids=new[]{id}},cancellationToken);
        var items=ParseToday(data,1);
        if(items.Count==0)return null;
        if(items[0].Id!=id)throw new MaiMemoException("今日学习数据与当前单词不一致，请稍后重试。");
        return items[0];
    }
    private static IReadOnlyList<TodayStudyItem> ParseToday(JsonElement data,int max)
    {
        if(!data.TryGetProperty("today_items",out var items)||items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()>max)
            throw new MaiMemoException("墨墨没有返回完整的今日学习数据，请先打开 App 并开启自动同步后重试。");
        var output=new List<TodayStudyItem>();var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in items.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)throw new MaiMemoException("今日学习数据不完整，请稍后重试。");
            var vocId=Text(item,"voc_id");
            if(vocId.Length==0||!item.TryGetProperty("is_new",out var isNew)||isNew.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                ||!item.TryGetProperty("is_finished",out var finished)||finished.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new MaiMemoException("今日学习数据不完整，请稍后重试。");
            if(!ids.Add(vocId))continue;
            var response=Text(item,"first_response");
            output.Add(new(vocId,Text(item,"voc_spelling"),isNew.GetBoolean(),finished.GetBoolean(),HasStudyResponse(response),item.TryGetProperty("order",out var order)&&order.TryGetInt32(out var n)?n:output.Count));
        }
        return output;
    }

    private static bool HasStudyResponse(string response)
    {
        if(response.StartsWith("STUDY_RESPONSE_",StringComparison.Ordinal))response=response[15..];
        return response is "FAMILIAR" or "VAGUE" or "FORGET" or "WELL_FAMILIAR" or "CANCEL_WELL_FAMILIAR";
    }
    private async Task<StudyRecordState> ReadStudiedWordAsync(string id,CancellationToken cancellationToken)
    {
        var data=await SendAsync(HttpMethod.Post,"memo/study/query_study_records",new {voc_ids=new[]{id},limit=1},cancellationToken);
        if(!data.TryGetProperty("records",out var records)||records.ValueKind!=JsonValueKind.Array||records.GetArrayLength()>1)
            throw new MaiMemoException("墨墨没有返回完整的学习记录，请稍后重试。");
        if(records.GetArrayLength()==0)return StudyRecordState.Absent;
        var item=records[0];
        if(item.ValueKind!=JsonValueKind.Object||Text(item,"voc_id")!=id||!item.TryGetProperty("study_count",out var count)||count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out var value)||value<0)
            throw new MaiMemoException("学习记录与当前单词不一致，请稍后重试。");
        // The live service also creates count=1 + placeholder dates on newly planned words.
        // Placeholder count=1 records do not establish either learned or unlearned status.
        var response=Text(item,"last_response");
        var familiar=item.TryGetProperty("tags",out var tags)&&(tags.ValueKind==JsonValueKind.String&&tags.GetString()=="WELL_FAMILIAR"||tags.ValueKind==JsonValueKind.Array&&tags.EnumerateArray().Any(tag=>tag.ValueKind==JsonValueKind.String&&tag.GetString()=="WELL_FAMILIAR"));
        if(value>1||HasStudyResponse(response)||familiar)return StudyRecordState.Learned;
        return value==0?StudyRecordState.Unstudied:StudyRecordState.Uncertain;
    }
}
