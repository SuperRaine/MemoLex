using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemoLex.Core;

namespace MemoLex.Services;

public sealed record Vocabulary(string Id, string Spelling);
public sealed record MemoNote(string Id, string NoteType, string Text, string Status);
public sealed record MemoInterpretation(string Id, string Text, IReadOnlyList<string> Tags, string Status);
public sealed record MemoPhrase(string Id, string Text, string Interpretation, string Origin, string Status, IReadOnlyList<string>? Tags = null);
public sealed record CloudNotepad(
    string Id, string Title, string Brief, IReadOnlyList<string> Tags, string Status,
    string Content, string Type, IReadOnlyList<string> Words, bool HasContent=true)
{
    public override string ToString() => Title;
}
public sealed record AddWordResult(CloudNotepad Notepad, bool AlreadyExisted);
public sealed record RemoveWordResult(CloudNotepad Notepad, bool AlreadyAbsent);

public class MaiMemoException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public MaiMemoException(string message, HttpStatusCode? statusCode = null) : base(message) => StatusCode = statusCode;
}
public sealed class EmptyNotepadException(CloudNotepad notepad) : MaiMemoException("云词本至少需要保留一个单词；如需全部移除，请确认删除整个云词本。")
{
    public CloudNotepad Notepad {get;}=notepad;
}

/// <summary>All authenticated requests are sent only to the fixed MaiMemo HTTPS origin.</summary>
public sealed partial class MaiMemoClient : IDisposable
{
    private const string ApiBase = "https://open.maimemo.com/open/api/v1/";
    private readonly TokenStore _tokens;
    private readonly HttpClient _http;
    private readonly TimeSpan _minimumRequestInterval;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private long _lastRequest;
    private bool _disposed;
    internal event Action<string,bool>? AuthenticationResult;
    private void ReportAuthentication(string token,bool accepted)=>AuthenticationResult?.Invoke(TokenStore.Fingerprint(token)!,accepted);

    public MaiMemoClient(TokenStore tokens) : this(tokens, new HttpClientHandler { AllowAutoRedirect = false }, TimeSpan.FromMilliseconds(1650)) { }

    internal MaiMemoClient(TokenStore tokens, HttpMessageHandler handler, TimeSpan minimumRequestInterval)
    {
        _tokens = tokens;
        _minimumRequestInterval = minimumRequestInterval;
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiBase),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MemoLex/"+(typeof(MaiMemoClient).Assembly.GetName().Version?.ToString(3)??"1.0.3"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<Vocabulary?> ResolveWordAsync(string spelling, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        spelling = CleanWord(spelling);
        try
        {
            var data = await SendAsync(HttpMethod.Get, "vocabulary?spelling=" + Uri.EscapeDataString(spelling), null, cancellationToken);
            if (!data.TryGetProperty("voc", out var voc) || voc.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            if (voc.ValueKind == JsonValueKind.Array) voc = voc.EnumerateArray().FirstOrDefault();
            var id = Text(voc, "id");
            return id.Length == 0 ? null : new Vocabulary(id, Text(voc, "spelling", spelling));
        }
        catch (MaiMemoException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public async Task<IReadOnlyList<MemoNote>> GetNotesAsync(string vocId, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        var data = await SendAsync(HttpMethod.Get, "notes?voc_id=" + Uri.EscapeDataString(vocId), null, cancellationToken);
        return Array(data, "notes").Select(note => new MemoNote(Text(note, "id"), MnemonicTypes.Normalize(Text(note, "note_type")),
            Text(note, "note"), Text(note, "status"))).Where(note => note.Status != "DELETED").ToList();
    }

    public async Task<MemoNote> SaveNoteAsync(string vocId,string? noteId,string noteType,string text,CancellationToken cancellationToken=default)
    {
        using var accountScope=BeginAccountOperation();
        text=text.Trim();noteType=noteType.Trim();
        noteId=string.IsNullOrWhiteSpace(noteId)?null:noteId;
        if(text.Length is <1 or >20000||!MnemonicTypes.IsAllowed(noteType)||string.IsNullOrWhiteSpace(vocId))throw new ArgumentException("请选择有效的助记分类并填写内容。");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            object body=string.IsNullOrWhiteSpace(noteId)?new {note=new {voc_id=vocId,note_type=noteType,note=text}}:new {note=new {note_type=noteType,note=text}};
            var data=await SendAsync(HttpMethod.Post,string.IsNullOrWhiteSpace(noteId)?"notes":"notes/"+Uri.EscapeDataString(noteId),body,cancellationToken);
            if(!data.TryGetProperty("note",out var note)||Text(note,"id").Length==0||noteId!=null&&Text(note,"id")!=noteId)
                throw new MaiMemoException("暂时无法确认助记是否保存成功，请重新查词查看。");
            if(Text(note,"status")=="DELETED")throw new MaiMemoException("这条云端助记已删除，请重新查词后操作。");
            return new(Text(note,"id"),MnemonicTypes.Normalize(Text(note,"note_type",noteType)),Text(note,"note",text),Text(note,"status","PUBLISHED"));
        }
        finally{_writeGate.Release();}
    }

    public async Task<IReadOnlyList<MemoInterpretation>> GetInterpretationsAsync(string vocId, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        var data = await SendAsync(HttpMethod.Get, "interpretations?voc_id=" + Uri.EscapeDataString(vocId), null, cancellationToken);
        return Array(data, "interpretations").Select(item => new MemoInterpretation(Text(item, "id"), Text(item, "interpretation"),
            Strings(item, "tags"), Text(item, "status"))).Where(item => item.Status != "DELETED").ToList();
    }

    public async Task<IReadOnlyList<MemoPhrase>> GetPhrasesAsync(string vocId, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        var data = await SendAsync(HttpMethod.Get, "phrases?voc_id=" + Uri.EscapeDataString(vocId), null, cancellationToken);
        return Array(data, "phrases").Select(item => new MemoPhrase(
                Text(item, "id"),
                Text(item, "phrase"),
                Text(item, "interpretation"),
                Text(item, "origin"),
                Text(item, "status"),
                Strings(item, "tags")))
            .Where(item => item.Status != "DELETED" && item.Text.Length > 0)
            .ToList();
    }

    public async Task<MemoPhrase> SavePhraseAsync(
        string vocId,
        string? phraseId,
        string phrase,
        string interpretation,
        IReadOnlyList<string>? tags,
        string origin,
        CancellationToken cancellationToken = default)
    {
        using var accountScope = BeginAccountOperation();
        phrase = (phrase ?? "").Trim();
        interpretation = (interpretation ?? "").Trim();
        origin = string.IsNullOrWhiteSpace(origin) ? "墨墨搜词" : origin.Trim();
        if (origin.Length > 100) origin = origin[..100];
        phraseId = string.IsNullOrWhiteSpace(phraseId) ? null : phraseId.Trim();
        if (phrase.Length is < 1 or > 2000 || interpretation.Length > 2000 || string.IsNullOrWhiteSpace(vocId))
            throw new ArgumentException("请填写有效的英文例句内容（1–2000 个字符）。");
        var cleanTags = (tags ?? System.Array.Empty<string>())
            .Select(tag => (tag ?? "").Trim())
            .Where(tag => tag.Length > 0)
            .Select(tag => tag.Length > 30 ? tag[..30] : tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            object body = phraseId == null
                ? new { phrase = new { voc_id = vocId, phrase, interpretation, tags = cleanTags, origin } }
                : new { phrase = new { phrase, interpretation, tags = cleanTags, origin } };
            var path = phraseId == null ? "phrases" : "phrases/" + Uri.EscapeDataString(phraseId);
            var data = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
            if (!data.TryGetProperty("phrase", out var saved) || Text(saved, "id").Length == 0 || (phraseId != null && Text(saved, "id") != phraseId))
                throw new MaiMemoException("暂时无法确认例句是否同步成功，请重新查词查看。");
            if (Text(saved, "status") == "DELETED")
                throw new MaiMemoException("这条云端例句已删除，请重新查词后操作。");
            var returnedTags = Strings(saved, "tags");
            return new MemoPhrase(
                Text(saved, "id"),
                Text(saved, "phrase", phrase),
                Text(saved, "interpretation", interpretation),
                Text(saved, "origin", origin),
                Text(saved, "status", "PUBLISHED"),
                returnedTags.Count > 0 || cleanTags.Length == 0 ? returnedTags : cleanTags);
        }
        finally { _writeGate.Release(); }
    }

    public async Task DeletePhraseAsync(string phraseId, string? vocId = null, CancellationToken cancellationToken = default)
    {
        using var accountScope = BeginAccountOperation();
        phraseId = (phraseId ?? "").Trim();
        if (phraseId.Length == 0) throw new ArgumentException("无效云端例句 ID。", nameof(phraseId));
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var path = "phrases/" + Uri.EscapeDataString(phraseId);
            JsonElement data = default;
            try { data = await SendAsync(HttpMethod.Delete, path, null, cancellationToken); }
            catch (MaiMemoException error) when (error.StatusCode == HttpStatusCode.NotFound) { return; }
            catch (MaiMemoException error) when (error.StatusCode == null)
            {
                // Verify via list if possible; never auto-retry DELETE.
            }
            if (data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("phrase", out var deleted)
                && Text(deleted, "id") == phraseId
                && Text(deleted, "status") == "DELETED")
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(vocId))
            {
                try
                {
                    var latest = await GetPhrasesAsync(vocId, cancellationToken);
                    if (!latest.Any(item => item.Id == phraseId)) return;
                }
                catch (MaiMemoException) { }
            }
            throw new MaiMemoException("云端例句删除结果暂未确认，请重新查词检查后再操作。");
        }
        finally { _writeGate.Release(); }
    }

    public async Task<IReadOnlyList<CloudNotepad>> ListNotepadsAsync(CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        const int pageSize = 10;
        var output = new List<CloudNotepad>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < 100000; offset += pageSize)
        {
            var data = await SendAsync(HttpMethod.Get, $"notepads?limit={pageSize}&offset={offset}", null, cancellationToken);
            var page = Array(data, "notepads").Select(ParseNotepad).ToList();
            var newItems = 0;
            foreach (var pad in page)
            {
                if (pad.Id.Length > 0 && seen.Add(pad.Id))
                {
                    newItems++;
                    if (pad.Status != "DELETED" && pad.Type != "FAVORITE") output.Add(pad);
                }
            }
            if (page.Count < pageSize || newItems == 0) return output;
        }
        throw new MaiMemoException("云词本数量过多，请稍后重试。");
    }

    public async Task<CloudNotepad> GetNotepadAsync(string id, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        var data = await SendAsync(HttpMethod.Get, "notepads/" + Uri.EscapeDataString(id), null, cancellationToken);
        if (!data.TryGetProperty("notepad", out var pad)) throw new MaiMemoException("未能加载完整词本，请刷新后重试。");
        if (Text(pad, "id") != id) throw new MaiMemoException("返回的云词本与所选词本不一致，请刷新后重试。");
        if (pad.ValueKind != JsonValueKind.Object || !pad.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String
            || !pad.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
            || !pad.TryGetProperty("brief", out var brief) || brief.ValueKind != JsonValueKind.String
            || !pad.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array
            || !pad.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
            throw new MaiMemoException("词本未加载完整，已停止修改。请刷新后重试。");
        return ParseNotepad(pad);
    }

    public async Task<AddWordResult> AddToNotepadAsync(string id, string word, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        word = CleanWord(word);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            // Always fetch the latest full content before updating. Never rebuild a user's cloud word list from a brief list response.
            var current = await GetNotepadAsync(id, cancellationToken);
            if (current.Type == "FAVORITE") throw new MaiMemoException("请先选择一个普通云词本。");
            if (current.Status == "DELETED") throw new MaiMemoException("这个云词本已被删除，请重新选择。");
            if (ContainsWord(current, word)) return new AddWordResult(current, true);
            var content = current.Content;
            if (content.Length > 0 && !content.EndsWith('\n') && !content.EndsWith('\r')) content += "\n";
            content += word;
            return new AddWordResult(await SaveNotepadAsync(current,current.Title,current.Brief,content,cancellationToken),false);
        }
        finally { _writeGate.Release(); }
    }

    public Task<CloudNotepad> CreateNotepadAsync(string title, CancellationToken cancellationToken) =>
        CreateNotepadAsync(title, initialWord: null, cancellationToken);

    public async Task<RemoveWordResult> RemoveFromNotepadAsync(string id, string word, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        word = CleanWord(word);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var current = await GetNotepadAsync(id, cancellationToken);
            EnsureEditable(current);
            if (!ContainsWord(current, word)) return new(current, true);
            var content = NotepadContent.RemoveWords(current.Content, [word]);
            if (content == current.Content) throw new MaiMemoException("这个单词位于词组或说明中，请在「单词与释义」中编辑词表后移除。");
            if(NotepadContent.Words(content).Count==0)throw new EmptyNotepadException(current);
            return new(await SaveNotepadAsync(current, current.Title, current.Brief, content, cancellationToken), false);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<CloudNotepad> UpdateNotepadAsync(CloudNotepad original, string title, string brief, string content, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        title = title.Trim();
        if (title.Length is < 1 or > 100) throw new ArgumentException("云词本名称需为 1–100 个字符。");
        if (brief.Length > 2000 || content.Length > 300000) throw new ArgumentException("简介或词本内容过长，请缩短后保存。");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var current = await GetNotepadAsync(original.Id, cancellationToken);
            EnsureEditable(current);
            if (current.Title != original.Title || current.Brief != original.Brief || current.Content != original.Content
                || current.Status != original.Status || !current.Tags.SequenceEqual(original.Tags))
                throw new MaiMemoException("词本已在其他设备上更新。请先复制当前修改，再重新加载词本并合并保存。");
            if (current.Title == title && current.Brief == brief && current.Content == content) return current;
            if(NotepadContent.Words(content).Count==0)throw new EmptyNotepadException(current);
            return await SaveNotepadAsync(current, title, brief, content, cancellationToken);
        }
        finally { _writeGate.Release(); }
    }

    private static void EnsureEditable(CloudNotepad pad)
    {
        if (pad.Type == "FAVORITE" || pad.Status == "DELETED") throw new MaiMemoException("这个词本暂时不能编辑，请重新选择普通云词本。");
    }
    public async Task<CloudNotepad> DeleteNotepadAsync(CloudNotepad original,CancellationToken cancellationToken=default)
    {
        using var accountScope=BeginAccountOperation();
        EnsureEditable(original);
        if(string.IsNullOrWhiteSpace(original.Id))throw new ArgumentException("无效云词本。");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            CloudNotepad current;
            try{current=await GetNotepadAsync(original.Id,cancellationToken);}
            catch(MaiMemoException error)when(error.StatusCode==HttpStatusCode.NotFound){return original with {Status="DELETED"};}
            if(current.Id!=original.Id)throw new MaiMemoException("云词本 ID 不一致，已停止删除。");
            if(current.Status=="DELETED")return current;
            EnsureEditable(current);
            if(current.Title!=original.Title||current.Brief!=original.Brief||current.Content!=original.Content||current.Status!=original.Status||!current.Tags.SequenceEqual(original.Tags))
                throw new MaiMemoException("词本已在其他设备上更新，已停止删除。请重新加载后确认。");
            var path="notepads/"+Uri.EscapeDataString(original.Id);
            var deleted=current with {Status="DELETED"};
            JsonElement data=default;
            try{data=await SendAsync(HttpMethod.Delete,path,null,cancellationToken);}
            catch(MaiMemoException error)when(error.StatusCode==HttpStatusCode.NotFound){return deleted;}
            catch(MaiMemoException error)when(error.StatusCode==null)
            {
                // The server may have completed the deletion before a short,
                // unreadable or interrupted response reached this client.
                // Verify with GET below; never repeat the DELETE automatically.
            }
            bool Confirmed(JsonElement response)=>response.ValueKind==JsonValueKind.Object
                &&response.TryGetProperty("notepad",out var pad)&&Text(pad,"id")==original.Id&&Text(pad,"status")=="DELETED";
            if(Confirmed(data))return deleted;
            for(var attempt=0;attempt<2;attempt++)
            {
                try
                {
                    // Read the raw snapshot because deleted records can omit
                    // the content and metadata required by the editor.
                    if(Confirmed(await SendAsync(HttpMethod.Get,path,null,cancellationToken)))return deleted;
                }
                catch(MaiMemoException error)when(error.StatusCode==HttpStatusCode.NotFound){return deleted;}
                catch(MaiMemoException){break;}
            }
            throw new MaiMemoException("删除结果暂未确认，请刷新云词本列表检查后再操作。");
        }
        finally{_writeGate.Release();}
    }
    private async Task<CloudNotepad> SaveNotepadAsync(CloudNotepad current, string title, string brief, string content, CancellationToken cancellationToken)
    {
        var data = await SendAsync(HttpMethod.Post, "notepads/" + Uri.EscapeDataString(current.Id),
            new { notepad = new { title, brief, content, tags = current.Tags, status = current.Status } }, cancellationToken);
        if (!data.TryGetProperty("notepad", out var saved)) throw new MaiMemoException("墨墨未返回保存结果，请重新加载词本确认后再操作。");
        if(Text(saved,"id")!=current.Id)throw new MaiMemoException("暂时无法确认词本是否保存成功，请重新加载后查看。");
        var returned=ParseNotepad(saved);
        if(returned.HasContent&&returned.Content.Replace("\r\n","\n")!=content.Replace("\r\n","\n"))
            throw new MaiMemoException("词本的保存结果与预期不符，请重新加载后查看。");
        if(saved.TryGetProperty("status",out _)&&returned.Status!=current.Status)
            throw new MaiMemoException("词本的公开状态与预期不符，请重新加载后查看。");
        // Update responses may contain only brief fields, or a stale parsed list.
        // Acknowledged submitted content is the membership source of truth.
        return current with {Title=title,Brief=brief,Content=content,Words=NotepadContent.Words(content),HasContent=true};
    }

    public async Task<CloudNotepad> CreateNotepadAsync(string title, string? initialWord = null, CancellationToken cancellationToken = default)
    {
        using var accountScope=BeginAccountOperation();
        title = (title ?? "").Trim();
        if (title.Length is < 1 or > 100) throw new ArgumentException("云词本名称需为 1–100 个字符。", nameof(title));
        var content = string.IsNullOrWhiteSpace(initialWord) ? "" : CleanWord(initialWord);
        var body = new { notepad = new { title, brief = "从 MemoLex 桌面查词工具收藏的单词", content,
            tags = System.Array.Empty<string>(), status = "UNPUBLISHED" } };
        var data = await SendAsync(HttpMethod.Post, "notepads", body, cancellationToken);
        if (!data.TryGetProperty("notepad", out var pad)) throw new MaiMemoException("墨墨未返回新建结果，请刷新列表确认。");
        var created=ParseNotepad(pad);
        if(created.Id.Length==0)throw new MaiMemoException("墨墨未返回新词本 ID，请刷新列表确认。");
        return created with {Title=title,Content=content,Words=NotepadContent.Words(content),HasContent=true};
    }

    public static bool ContainsWord(CloudNotepad notepad, string word)
    {
        if (!notepad.HasContent) return notepad.Words.Any(value => string.Equals(value.Trim(), word.Trim(), StringComparison.OrdinalIgnoreCase));
        return notepad.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(value => !value.TrimStart().StartsWith('#') && string.Equals(value.Trim(), word.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (relativePath.StartsWith('/') || !Uri.TryCreate(relativePath, UriKind.Relative, out _))
            throw new ArgumentException("无效接口路径。", nameof(relativePath));
        var studyRequest=relativePath.StartsWith("memo/study/",StringComparison.Ordinal);
        var studyWrite=relativePath=="memo/study/add_words";
        var retryableRead=method==HttpMethod.Get||relativePath is "memo/study/get_study_progress" or "memo/study/get_today_items" or "memo/study/query_study_records";
        var token = _tokens.GetToken();
        if (string.IsNullOrWhiteSpace(token)) throw new MaiMemoException("请在设置中登录墨墨账号。");
        var credential=TokenStore.Fingerprint(token)!;
        ValidateAccount(_operationCredential.Value??credential);
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            for(var attempt=0;;attempt++)
            {
            // 1.65s between requests stays below the official 40/minute limit.
            if (_lastRequest != 0)
            {
                var remaining = _minimumRequestInterval - Stopwatch.GetElapsedTime(_lastRequest);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ValidateAccount(credential);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            _lastRequest = Stopwatch.GetTimestamp();
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            ValidateAccount(credential);
            if (!response.IsSuccessStatusCode)
            {
                if(response.StatusCode==HttpStatusCode.Unauthorized)
                {
                    await RecheckAuthenticationAsync(token,credential,cancellationToken,attempt==0&&retryableRead);
                    continue;
                }
                if(studyRequest&&!studyWrite&&response.StatusCode==HttpStatusCode.Forbidden)
                    await RecheckCredentialAsync(token,credential,cancellationToken);
                if(studyWrite&&(int)response.StatusCode>=500)throw new StudyAddUncertainException("墨墨暂未确认添加结果，请核对今日新学。");
                throw SafeHttpError(response.StatusCode);
            }
            if (response.Content.Headers.ContentLength is > 16 * 1024 * 1024) throw new MaiMemoException("墨墨返回的数据过大，请使用较小的云词本。");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[16384];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, timeout.Token);
                if (count == 0) break;
                if (memory.Length + count > 16 * 1024 * 1024) throw new MaiMemoException("墨墨返回的数据过大，请使用较小的云词本。");
                memory.Write(buffer, 0, count);
            }
            memory.Position = 0;
            using var json = await JsonDocument.ParseAsync(memory, cancellationToken: timeout.Token);
            ValidateAccount(credential);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw studyWrite?new StudyAddUncertainException("墨墨没有返回有效的添加结果，请核对今日新学。"):new MaiMemoException("墨墨返回的数据格式有变化，请稍后重试。");
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            {
                var error=SafeApiError(root);
                if(error.StatusCode==HttpStatusCode.Unauthorized)
                {
                    await RecheckAuthenticationAsync(token,credential,cancellationToken,attempt==0&&retryableRead);
                    continue;
                }
                if(studyRequest&&!studyWrite&&error.StatusCode==HttpStatusCode.Forbidden)
                    await RecheckCredentialAsync(token,credential,cancellationToken);
                throw error;
            }
            if (root.TryGetProperty("data", out var data))
            {
                if (data.ValueKind != JsonValueKind.Object) throw studyWrite?new StudyAddUncertainException("墨墨没有返回有效的添加结果，请核对今日新学。"):new MaiMemoException("墨墨没有返回可用数据，请刷新后重试。");
                ReportAuthentication(token,true);return data.Clone();
            }
            ReportAuthentication(token,true);return root.Clone();
            }
        }
        catch (AccountChangedException){throw;}
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var targetName = relativePath.StartsWith("phrases", StringComparison.Ordinal) ? "例句"
                : relativePath.StartsWith("notes", StringComparison.Ordinal) ? "助记" : "云词本";
            if(studyWrite)throw new StudyAddUncertainException("添加请求超时，结果尚未确认，请核对今日新学。");
            if(studyRequest)throw new MaiMemoException("读取学习数据超时，请确认墨墨 App 已开启自动同步后重试。");
            if(method==HttpMethod.Delete)throw new MaiMemoException($"请求超时，删除结果尚未确认。请刷新{targetName}检查后再操作。");
            throw new MaiMemoException(method == HttpMethod.Post
                ? $"请求超时，保存结果尚未确认。请刷新{targetName}检查后再操作。"
                : "连接墨墨超时，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            var targetName = relativePath.StartsWith("phrases", StringComparison.Ordinal) ? "例句"
                : relativePath.StartsWith("notes", StringComparison.Ordinal) ? "助记" : "云词本";
            if(studyWrite)throw new StudyAddUncertainException("添加时连接中断，结果尚未确认，请核对今日新学。");
            if(studyRequest)throw new MaiMemoException("暂时无法读取学习数据，请检查网络及墨墨 App 的自动同步。");
            if(method==HttpMethod.Delete)throw new MaiMemoException($"连接中断，删除结果尚未确认。请刷新{targetName}检查后再操作。");
            throw new MaiMemoException(method == HttpMethod.Post
                ? $"网络连接中断，保存结果尚未确认。请刷新{targetName}检查后再操作。"
                : "暂时无法连接墨墨，请检查网络后重试。");
        }
        catch (JsonException)
        {
            var targetName = relativePath.StartsWith("phrases", StringComparison.Ordinal) ? "例句"
                : relativePath.StartsWith("notes", StringComparison.Ordinal) ? "助记" : "云词本";
            if(studyWrite)throw new StudyAddUncertainException("添加结果无法读取，请核对今日新学。");
            if(studyRequest)throw new MaiMemoException("墨墨的学习数据格式有变化，请稍后重试。");
            if(method==HttpMethod.Delete)throw new MaiMemoException($"删除结果无法读取，请刷新{targetName}确认。");
            throw new MaiMemoException(method == HttpMethod.Post
                ? $"墨墨的保存结果无法读取。请先刷新{targetName}确认结果，再决定是否重试。"
                : "墨墨返回的数据格式有变化，请稍后重试。");
        }
        finally { _requestGate.Release(); }
    }

    private static MaiMemoException SafeHttpError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new("墨墨登录已失效，请在设置中重新登录。", status),
        HttpStatusCode.Forbidden => new("当前账号没有此接口的访问权限，请检查墨墨开放 API 的授权。", status),
        HttpStatusCode.NotFound => new("墨墨中未找到对应单词、例句或云词本。", status),
        HttpStatusCode.TooManyRequests => new("请求过于频繁，请稍等一分钟后重试。", status),
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new("墨墨未接受此次操作，请检查填写内容后重试。", status),
        _ when (int)status >= 500 => new("墨墨服务暂时不可用，请稍后重试。", status),
        _ => new("墨墨请求失败，请刷新后重试。", status)
    };

    private static MaiMemoException SafeApiError(JsonElement root)
    {
        // Inspect classification only. Never expose upstream bodies or supplied credentials in an exception.
        var errors=string.Join(" ",new[]{"errors","error","code","message"}
            .Where(key=>root.TryGetProperty(key,out _))
            .Select(key=>root.GetProperty(key).ToString())).ToUpperInvariant();
        if (Regex.IsMatch(errors,@"(?<![A-Z0-9])(?:RATE(?:[ _-]LIMIT)?|TOO_MANY(?:_REQUESTS)?|QUOTA(?:_EXCEEDED)?|LIMIT_EXCEEDED)(?![A-Z0-9])",RegexOptions.CultureInvariant))
            return new MaiMemoException("请求过于频繁，请稍等一分钟后重试。", HttpStatusCode.TooManyRequests);
        if (Regex.IsMatch(errors,@"(?<![A-Z0-9])(?:UNAUTHORIZED|UNAUTHENTICATED|AUTHENTICATION_FAILED|INVALID_(?:API_)?TOKEN|(?:API_)?TOKEN_(?:INVALID|EXPIRED|REVOKED|MISSING|NOT_FOUND)|EXPIRED_TOKEN|INVALID_CREDENTIALS?|INVALID_SESSION|SESSION_(?:EXPIRED|REVOKED|REPLACED)|LOGIN_EXPIRED)(?![A-Z0-9])",RegexOptions.CultureInvariant)
            ||Regex.IsMatch(errors,@"\b(?:TOKEN|AUTHENTICATION)\b[^\r\n]{0,40}\b(?:INVALID|EXPIRED|REVOKED)\b",RegexOptions.CultureInvariant))
            return new MaiMemoException("墨墨登录已失效，请在设置中重新登录。", HttpStatusCode.Unauthorized);
        if(errors.Contains("FORBIDDEN")||errors.Contains("PERMISSION")||errors.Contains("SCOPE")||errors.Contains("ACCESS_DENIED"))
            return new MaiMemoException("当前账号没有此接口的访问权限，请检查墨墨开放 API 的授权。",HttpStatusCode.Forbidden);
        if (errors.Contains("NOT_FOUND") || errors.Contains("NOT FOUND"))
            return new MaiMemoException("墨墨中未找到此单词或云词本。", HttpStatusCode.NotFound);
        return new MaiMemoException("墨墨未接受此次操作，请检查账户权限后重试。");
    }

    private static string CleanWord(string word)
    {
        word = (word ?? "").Trim();
        if (word.Length is < 1 or > 200 || word.Any(char.IsControl) || word.StartsWith('#'))
            throw new ArgumentException("请输入有效单词或短语。", nameof(word));
        return word;
    }

    private static CloudNotepad ParseNotepad(JsonElement item)
    {
        var content = Text(item, "content");
        var hasContent = item.TryGetProperty("content", out var value) && value.ValueKind == JsonValueKind.String;
        var words = new List<string>();
        if (!hasContent)
        {
            foreach (var entry in Array(item, "list"))
            {
                if (Text(entry, "type") != "WORD" || !entry.TryGetProperty("data", out var data)) continue;
                var word = Text(data, "word");
                if (word.Length > 0) words.Add(word);
            }
        }
        return new CloudNotepad(Text(item, "id"), Text(item, "title", "未命名云词本"), Text(item, "brief"),
            Strings(item, "tags"), Text(item, "status", "PUBLISHED"), content, Text(item, "type", "NOTEPAD"),
            hasContent ? NotepadContent.Words(content) : words, hasContent);
    }

    private static IEnumerable<JsonElement> Array(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray() : System.Array.Empty<JsonElement>();

    private static IReadOnlyList<string> Strings(JsonElement source, string key) => Array(source, key)
        .Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString() ?? "").ToArray();

    private static string Text(JsonElement source, string key, string fallback = "")
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number => value.GetRawText(),
            _ => fallback
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        // Semaphore objects are intentionally left alive for any canceled in-flight operations releasing their gates.
    }
}
