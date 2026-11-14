using System.Net.Http;
using System.Text.Json;

namespace MemoLex.Services;

public enum TodayStudyStatus { Added, AlreadyToday, AlreadyLearned, NotAdded, PendingVerification }
public sealed record TodayStudyResult(TodayStudyStatus Status,int? AddedCount=null);
internal sealed class StudyAddUncertainException(string message) : MaiMemoException(message);

public sealed partial class MaiMemoClient
{
    /// <summary>Reads learning status without adding a word or advancing review.</summary>
    public async Task<TodayStudyStatus> GetTodayStudyStatusAsync(Vocabulary word,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(word);
        if(string.IsNullOrWhiteSpace(word.Id)||word.Id.Length>200||word.Id.Any(char.IsControl))throw new ArgumentException("无效的墨墨单词 ID。");
        var studied=await ReadStudiedWordAsync(word.Id,cancellationToken);
        if(studied==true)return TodayStudyStatus.AlreadyLearned;
        var today=await ReadTodayWordAsync(word.Id,cancellationToken);
        return today.HasValue?(today.Value?TodayStudyStatus.AlreadyToday:TodayStudyStatus.AlreadyLearned):TodayStudyStatus.NotAdded;
    }

    /// <summary>Adds an unstudied word, advances it to today and verifies the server's new-word flag.</summary>
    public async Task<TodayStudyResult> AddToTodayAsync(Vocabulary word,bool verifyOnly=false,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(word);
        if(string.IsNullOrWhiteSpace(word.Id)||word.Id.Length>200||word.Id.Any(char.IsControl))throw new ArgumentException("无效的墨墨单词 ID。");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var today=await ReadTodayWordAsync(word.Id,cancellationToken);
            if(today.HasValue)return new(today.Value?TodayStudyStatus.AlreadyToday:TodayStudyStatus.AlreadyLearned);
            var record=await ReadStudiedWordAsync(word.Id,cancellationToken);
            if(record==true)return new(TodayStudyStatus.AlreadyLearned);
            if(verifyOnly)return new(record.HasValue?TodayStudyStatus.PendingVerification:TodayStudyStatus.NotAdded);

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
                if(today.HasValue)return new(today.Value?(added==1?TodayStudyStatus.Added:TodayStudyStatus.AlreadyToday):TodayStudyStatus.AlreadyLearned,added);
            }
            catch(MaiMemoException) { return new(TodayStudyStatus.PendingVerification,added); }
            return new(added==0?TodayStudyStatus.NotAdded:TodayStudyStatus.PendingVerification,added);
        }
        finally{_writeGate.Release();}
    }

    private async Task<bool?> ReadTodayWordAsync(string id,CancellationToken cancellationToken)
    {
        var data=await SendAsync(HttpMethod.Post,"memo/study/get_today_items",new {voc_ids=new[]{id}},cancellationToken);
        if(!data.TryGetProperty("today_items",out var items)||items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()>1)
            throw new MaiMemoException("墨墨没有返回完整的今日学习数据，请先打开 App 并开启自动同步后重试。");
        if(items.GetArrayLength()==0)return null;
        var item=items[0];
        if(item.ValueKind!=JsonValueKind.Object||Text(item,"voc_id")!=id||!item.TryGetProperty("is_new",out var isNew)||isNew.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new MaiMemoException("今日学习数据与当前单词不一致，请稍后重试。");
        return isNew.GetBoolean();
    }

    // null: absent; false: in the plan but unstudied; true: previously studied.
    private async Task<bool?> ReadStudiedWordAsync(string id,CancellationToken cancellationToken)
    {
        var data=await SendAsync(HttpMethod.Post,"memo/study/query_study_records",new {voc_ids=new[]{id},limit=1},cancellationToken);
        if(!data.TryGetProperty("records",out var records)||records.ValueKind!=JsonValueKind.Array||records.GetArrayLength()>1)
            throw new MaiMemoException("墨墨没有返回完整的学习记录，请稍后重试。");
        if(records.GetArrayLength()==0)return null;
        var item=records[0];
        if(item.ValueKind!=JsonValueKind.Object||Text(item,"voc_id")!=id||!item.TryGetProperty("study_count",out var count)||count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out var value)||value<0)
            throw new MaiMemoException("学习记录与当前单词不一致，请稍后重试。");
        return value>0||!string.IsNullOrWhiteSpace(Text(item,"first_study_date"));
    }
}
