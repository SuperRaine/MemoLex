using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MemoLex.Core;

namespace MemoLex.Services;

public sealed record TodayStudyPlan(string Day, int Target, int Finished, IReadOnlyList<TodayStudyItem> Items)
{
    public int? NewWordsNeeded => Target > 0 && Finished == Items.Count(item => item.IsFinished)
        && (Items.Count < 1000 || Target <= Items.Count) && Items.Count > 0
        ? Math.Max(0, Target - Items.Count) : null;
}

public sealed record TodayStudyCandidates(IReadOnlyList<Vocabulary> Words, int Excluded);
public sealed record TodayStudyBatch(int Selected, int Eligible, int? Added)
{
    public string Summary => Added == null ? "添加结果待确认，请在墨墨背单词核对后再试"
        : Eligible == 0 ? "所选单词都已加入计划或已学过"
        : Added == Selected ? $"已提交 {Added} 个新词，手机端同步后可查看"
        : $"墨墨接收 {Added} 个新词，另有 {Selected - Added} 个未加入";
}

public sealed partial class MaiMemoClient
{
    internal const int StudyBatchLimit = 1000;
    internal static string TodayPlanDay() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<TodayStudyPlan> GetTodayPlanAsync(CancellationToken cancellationToken = default)
    {
        using var account = BeginAccountOperation();
        var day = TodayPlanDay();
        var data = await SendAsync(HttpMethod.Post, "memo/study/get_study_progress", null, cancellationToken);
        if (!data.TryGetProperty("progress", out var progress) || progress.ValueKind != JsonValueKind.Object
            || !progress.TryGetProperty("finished", out var done) || done.ValueKind != JsonValueKind.Number || !done.TryGetInt32(out var finished) || finished < 0
            || !progress.TryGetProperty("total", out var total) || total.ValueKind != JsonValueKind.Number || !total.TryGetInt32(out var target) || target < 0)
            throw new MaiMemoException("墨墨未返回完整的今日学习进度");

        var items = await GetTodayItemsAsync(cancellationToken);
        CheckStudyDay(day);
        return new(day, target, finished, items);
    }

    public async Task<TodayStudyCandidates> GetTodayCandidatesAsync(string? notepadId, string? input, CancellationToken cancellationToken = default)
    {
        using var account = BeginAccountOperation();
        var day = TodayPlanDay();
        IReadOnlyList<string> spellings;
        if (!string.IsNullOrWhiteSpace(notepadId))
        {
            var pad = await GetNotepadAsync(notepadId, cancellationToken);
            spellings = pad.Words.SelectMany(word => word.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(word => word.Length <= 200 && !word.Any(char.IsControl)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        else
        {
            spellings = NotepadContent.ParseWords(input ?? "");
            if (spellings.Count > StudyBatchLimit) throw new ArgumentException("一次最多输入 1000 个单词");
        }

        var eligible = new List<Vocabulary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in spellings.Chunk(StudyBatchLimit))
        {
            var data = await SendAsync(HttpMethod.Post, "memo/vocabulary/query", new { spellings = chunk }, cancellationToken);
            var exact = chunk.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = ReadStudyArray(data, "voc", chunk.Length).Select(item => new Vocabulary(Text(item, "id"), Text(item, "spelling")))
                .Where(item => IsStudyId(item.Id) && exact.Contains(item.Spelling) && seen.Add(item.Id)).ToArray();
            var available = await UnplannedIdsAsync(candidates.Select(item => item.Id).ToArray(), cancellationToken);
            eligible.AddRange(candidates.Where(item => available.Contains(item.Id)));
        }

        CheckStudyDay(day);
        return new(eligible, spellings.Count - eligible.Count);
    }

    public async Task<TodayStudyBatch> AddTodayBatchAsync(IReadOnlyList<string> selectedIds, string day, CancellationToken cancellationToken = default)
    {
        using var account = BeginAccountOperation();
        if (selectedIds.Count is < 1 or > StudyBatchLimit || selectedIds.Any(id => !IsStudyId(id)))
            throw new ArgumentException("请选择 1 到 1000 个有效单词");

        var ids = selectedIds.Distinct(StringComparer.Ordinal).ToArray();
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            CheckStudyDay(day);
            var available = await UnplannedIdsAsync(ids, cancellationToken);
            var words = ids.Where(available.Contains).Select(id => new { id }).ToArray();
            CheckStudyDay(day);
            if (words.Length == 0) return new(ids.Length, 0, 0);

            try
            {
                var data = await SendAsync(HttpMethod.Post, "memo/study/add_words", new { words, advance = true }, cancellationToken);
                var added = data.TryGetProperty("added_count", out var count) && count.ValueKind == JsonValueKind.Number
                    && count.TryGetInt32(out var value) && value >= 0 && value <= words.Length ? (int?)value : null;
                return new(ids.Length, words.Length, added);
            }
            catch (StudyAddUncertainException)
            {
                return new(ids.Length, words.Length, null);
            }
        }
        finally { _writeGate.Release(); }
    }

    private async Task<HashSet<string>> UnplannedIdsAsync(string[] ids, CancellationToken cancellationToken)
    {
        var remaining = ids.ToHashSet(StringComparer.Ordinal);
        if (remaining.Count == 0) return remaining;

        var today = await SendAsync(HttpMethod.Post, "memo/study/get_today_items", new { voc_ids = ids, limit = ids.Length }, cancellationToken);
        foreach (var item in ParseToday(today, ids.Length))
            if (!remaining.Remove(item.Id)) throw new MaiMemoException("墨墨返回的今日词表与所选单词不一致，请刷新后重试");

        if (remaining.Count == 0) return remaining;
        var unknown = remaining.ToArray();
        var history = await SendAsync(HttpMethod.Post, "memo/study/query_study_records", new { voc_ids = unknown, limit = unknown.Length }, cancellationToken);
        foreach (var item in ReadStudyArray(history, "records", unknown.Length))
            if (!remaining.Remove(Text(item, "voc_id"))) throw new MaiMemoException("墨墨返回的学习记录与所选单词不一致，请刷新后重试");

        return remaining;
    }

    private static JsonElement.ArrayEnumerator ReadStudyArray(JsonElement data, string field, int maximum)
    {
        if (!data.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum)
            throw new MaiMemoException("墨墨返回的数据不完整，请稍后重试");
        return value.EnumerateArray();
    }

    private static bool IsStudyId(string? id) => id is { Length: > 0 and <= 200 } && !string.IsNullOrWhiteSpace(id) && !id.Any(char.IsControl);

    private static void CheckStudyDay(string day)
    {
        if (day != TodayPlanDay()) throw new MaiMemoException("日期已变更，请刷新今日学习后重试");
    }
}
