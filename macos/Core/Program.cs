using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoLex.Core;
using MemoLex.Services;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }, MaxDepth = 32
    };
    private static readonly SemaphoreSlim Output = new(1, 1);

    private static async Task Main(string[] args)
    {
        Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
        var resources = Environment.GetEnvironmentVariable("MEMOLEX_RESOURCES") ?? AppContext.BaseDirectory;
        var profile = Environment.GetEnvironmentVariable("MEMOLEX_PROFILE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoLex");
        Directory.CreateDirectory(profile);
        using var service = new CoreService(resources, profile);
        if (args.Contains("--smoke-test"))
        {
            try { await Write(new { ok = true, result = await service.Handle("initialize", default, CancellationToken.None) }); }
            catch { await Write(new { ok = false }); Environment.ExitCode = 1; }
            return;
        }
        var running = new ConcurrentDictionary<string, CancellationTokenSource>();
        var tasks = new ConcurrentDictionary<string, Task>();
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (line.Length > 2_000_000) continue;
            JsonDocument document;
            try { document = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            using (document)
            {
                var root = document.RootElement;
                var id = Text(root, "id"); var method = Text(root, "method");
                if (id.Length is 0 or > 128 || method.Length is 0 or > 80) continue;
                var parameters = root.TryGetProperty("params", out var value) ? value.Clone() : default;
                if (method == "cancel")
                {
                    if (running.TryGetValue(Text(parameters, "id"), out var previous)) Cancel(previous);
                    continue;
                }
                var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                if (!running.TryAdd(id, cancellation)) { cancellation.Dispose(); continue; }
                var task = Run(id, method, parameters, cancellation);
                tasks[id] = task;
                _ = task.ContinueWith(completed => { tasks.TryRemove(id, out _); }, TaskScheduler.Default);
            }
        }
        foreach (var cancellation in running.Values) Cancel(cancellation);
        await Task.WhenAll(tasks.Values);

        async Task Run(string id, string method, JsonElement parameters, CancellationTokenSource cancellation)
        {
            try { await Write(new { id, result = await service.Handle(method, parameters, cancellation.Token) }); }
            catch (OperationCanceledException) { await Write(new { id, error = new { code = "cancelled", message = "操作已取消。" } }); }
            catch (Exception error)
            {
                var message = error is MaiMemoException or TranslationException or ExampleLookupException or ArgumentException or IOException or InvalidOperationException ? error.Message : "操作未完成，请重试。";
                var code = error switch
                {
                    MaiMemoException { StatusCode: HttpStatusCode.Unauthorized } => "unauthorized",
                    MaiMemoException { StatusCode: HttpStatusCode.Forbidden } => "permission_denied",
                    EmptyNotepadException => "empty_notepad",
                    _ => "operation_failed"
                };
                await Write(new { id, error = new { code, message, accountKey = Text(parameters, "accountKey") } });
            }
            finally { running.TryRemove(id, out _); cancellation.Dispose(); }
        }
    }
    private static void Cancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { /* The request completed concurrently. */ }
    }
    private static async Task Write(object value)
    {
        var line = JsonSerializer.Serialize(value, Json);
        await Output.WaitAsync();
        try { await Console.Out.WriteLineAsync(line); await Console.Out.FlushAsync(); }
        finally { Output.Release(); }
    }
    internal static string Text(JsonElement value, string name, string fallback = "") => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? fallback : fallback;
    internal static T Decode<T>(JsonElement value) => value.Deserialize<T>(Json) ?? throw new ArgumentException("数据不完整，请重新加载。");
}

internal sealed class CoreService : IDisposable
{
    private readonly LocalDictionary _dictionary;
    private readonly RelatedWords _related;
    private readonly ChineseReverseSearch _chinese;
    private readonly MnemonicStore _notes;
    private readonly ContextSentenceStore _contextSentences;
    private readonly SpellingMistakeStore _spellingMistakes;
    private readonly TokenStore _tokens = new();
    private readonly MaiMemoClient _api;
    private readonly ExampleSentenceClient _examples = new();
    private readonly TranslationClient _translation = new();
    private readonly PronunciationClient _pronunciation = new();
    private readonly OnlineDictionaryClient _onlineDictionary;
    private readonly Lazy<Task> _ready;
    private readonly object _accountGate = new();

    internal CoreService(string resources, string profile)
    {
        Environment.SetEnvironmentVariable("MEMOLEX_RESOURCES", resources);
        var data = Path.Combine(resources, "data");
        _dictionary = new(data);
        _related = new(_dictionary, data); _chinese = new(_dictionary, data);
        _notes = new(Path.Combine(profile, "mnemonics.user.json"), Path.Combine(data, "mnemonics.json"));
        _contextSentences = new(Path.Combine(profile, "context-sentences.json"));
        _spellingMistakes = new(Path.Combine(profile, "spelling-mistakes.json"));
        _api = new(_tokens);
        _onlineDictionary = new(_translation);
        _ready = new(async () => { await _dictionary.LoadAsync(); await Task.WhenAll(_related.LoadAsync(), _chinese.LoadAsync(), _notes.LoadAsync()); });
    }
    internal async Task<object?> Handle(string method, JsonElement p, CancellationToken cancellation)
    {
        if (method == "account.set")
        {
            lock (_accountGate)
            {
                _tokens.Set(string.IsNullOrWhiteSpace(T(p, "token")) ? null : T(p, "token"));
                _api.ExpectedCredentialKey = _tokens.CredentialKey;
                return new { authenticated = _tokens.HasToken, accountKey = _tokens.CredentialKey };
            }
        }
        if (method == "login.inspect") return OfficialLoginProtocol.Parse(T(p, "url"), T(p, "content"), DateTimeOffset.UtcNow);
        if (method == "login.allowed") return new { allowed = OfficialLoginProtocol.AllowedNavigation(T(p, "url")), tokenDocument = OfficialLoginProtocol.IsTokenDocument(T(p, "url")), startUrl = OfficialLoginProtocol.StartUrl };
        if (method == "login.validate")
        {
            var temporary = new TokenStore(); temporary.Set(T(p, "token"));
            using var client = new MaiMemoClient(temporary);
            return await client.ResolveWordAsync("hello", cancellation) ?? throw new MaiMemoException("暂时无法确认登录信息。");
        }
        if (method == "account.validate")
        {
            using var validation = _api.BindNativeAccount(T(p, "accountKey"));
            return await _api.ResolveWordAsync("hello", cancellation) ?? throw new MaiMemoException("暂时无法确认登录信息。");
        }
        if (method == "translate")
            return await _translation.TranslateAsync(T(p, "text"), T(p, "direction", "en|zh-CN"), cancellation);
        if (method == "examples.pronunciation")
            return new { audio = await _pronunciation.DownloadSentenceAsync(T(p, "text"), cancellation) };
        if (method == "pronunciation")
        {
            var accent = T(p, "accent") switch
            {
                "en-GB" => EnglishAccent.British,
                "en-US" => EnglishAccent.American,
                _ => throw new ArgumentException("请选择英音或美音。")
            };
            return new { audio = await _pronunciation.DownloadAsync(T(p, "word"), accent, cancellation) };
        }
        await _ready.Value.WaitAsync(cancellation);
        var authenticated = method.StartsWith("notepads.", StringComparison.Ordinal) || method.StartsWith("study.", StringComparison.Ordinal) || method is "notes.cloud" or "notes.publish" or "stubborn";
        var accountKey = _tokens.CredentialKey;
        using var scope = authenticated ? _api.BindNativeAccount(T(p, "accountKey")) : null;
        var word = T(p, "word").Trim();
        switch (method)
        {
            case "initialize": return new { version = typeof(CoreService).Assembly.GetName().Version?.ToString(3), wordCount = _dictionary.Count, localMnemonicCount = _notes.BundledCount, warning = _notes.LoadWarning, mnemonicTypes = MnemonicTypes.All, translationProvider = _translation.PreferredProvider };
            case "dictionary.lookup":
            {
                word = ExampleText.ResolveLookupWord(word, candidate => _dictionary.Lookup(candidate) != null);
                var entry = _dictionary.Lookup(word);
                if (entry != null) entry.translation = entry.DisplayMeaning;
                var spellingSuggestions = string.IsNullOrWhiteSpace(entry?.translation) ? _dictionary.SuggestSpellingCorrections(word, 5) : Array.Empty<string>();
                return new { entry, word, notes = DisplayNotes(word, _tokens.CredentialKey), suggestions = _dictionary.Suggest(word, 24), spellingSuggestions };
            }
            case "mnemonic.family":
                return _notes.FindMorphemeFamily(
                    T(p, "form"), T(p, "meaning"), T(p, "excludeWord"),
                    candidate => _dictionary.Lookup(candidate)?.DisplayMeaning,
                    Number(p, "limit", 18, 1, 40));
            case "mnemonic.practiceGroup":
            {
                HashSet<string>? priority = null;
                if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("priorityWords", out var pw) && pw.ValueKind == JsonValueKind.Array)
                    priority = pw.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return MorphemeGroupPractice.Build(
                    _notes,
                    T(p, "form"), T(p, "meaning"), T(p, "currentWord"),
                    candidate => _dictionary.Lookup(candidate)?.DisplayMeaning,
                    priority,
                    Number(p, "limit", 6, 2, 8));
            }
            case "contextSentences.list":
                return _contextSentences.List(Null(T(p, "word")));
            case "contextSentences.extract":
            {
                var paragraph = T(p, "paragraph");
                var matchedForm = Null(T(p, "matchedForm"));
                var extracted = ContextSentenceHelper.ExtractSentenceForWord(paragraph, word, matchedForm);
                var trans = ContextSentenceHelper.ExtractTranslationHint(paragraph, T(p, "translation"), extracted);
                return new { sentence = extracted, translation = trans, saved = _contextSentences.Contains(word, extracted) };
            }
            case "contextSentences.save":
            {
                var sentence = T(p, "sentence");
                var paragraph = T(p, "paragraph");
                var matchedForm = Null(T(p, "matchedForm"));
                if (string.IsNullOrWhiteSpace(sentence) && !string.IsNullOrWhiteSpace(paragraph))
                    sentence = ContextSentenceHelper.ExtractSentenceForWord(paragraph, word, matchedForm);
                var translation = T(p, "translation");
                if (!string.IsNullOrWhiteSpace(paragraph) && !string.IsNullOrWhiteSpace(translation))
                    translation = ContextSentenceHelper.ExtractTranslationHint(paragraph, translation, sentence);
                return _contextSentences.Save(word, sentence, translation, T(p, "source", "查词保存"), matchedForm);
            }
            case "contextSentences.remove":
                return new { removed = _contextSentences.Remove(T(p, "id")) };
            case "contextSentences.clear":
                return new { cleared = _contextSentences.Clear() };
            case "contextSentences.review":
                return _contextSentences.RecordReview(T(p, "id"));
            case "spelling.getDrill":
                return _spellingMistakes.GetActiveDrill(word);
            case "spelling.recordMistake":
                return _spellingMistakes.RecordMistake(word, T(p, "typed"));
            case "spelling.recordSuccess":
                _spellingMistakes.RecordCleanSuccess(word);
                return new { ok = true };
            case "spelling.active":
                return _spellingMistakes.ListActive();
            case "spelling.clear":
                return new { cleared = _spellingMistakes.Clear() };
            case "cache.stats":
                return new
                {
                    contextCount = _contextSentences.Count,
                    contextBytes = _contextSentences.FileSizeBytes,
                    spellingCount = _spellingMistakes.ActiveCount,
                    spellingBytes = _spellingMistakes.FileSizeBytes,
                    memoryCount = _examples.CachedCount + _onlineDictionary.CachedCount + _translation.CachedCount + _pronunciation.CachedCount,
                    memoryBytes = _pronunciation.CachedBytes
                };
            case "cache.clear":
                return new
                {
                    cleared = _examples.ClearCache() + _onlineDictionary.ClearCache() + _translation.ClearCache() + _pronunciation.ClearCache()
                };
            case "translation.extract":
                return VocabularyExtractor.Extract(T(p, "text"), _dictionary, Number(p, "limit", 24, 1, 60));
            case "dictionary.online":
                return string.IsNullOrEmpty(_dictionary.Lookup(word)?.DisplayMeaning)
                    ? await _onlineDictionary.LookupAsync(word, cancellation) : null;
            case "dictionary.suggest":
            {
                var list = _dictionary.Suggest(word, 30);
                if (list.Count == 0 && word.Length >= 3) list = _dictionary.SuggestSpellingCorrections(word, 10);
                return list;
            }
            case "dictionary.related": return await _related.LookupAsync(word, cancellation);
            case "dictionary.reverse": return await _chinese.SearchAsync(T(p, "query"), Number(p, "limit", 160, 1, 3000), cancellation);
            case "dictionary.batch": return NotepadContent.Describe(T(p, "content"), value => _dictionary.Lookup(value)?.translation, cancellation);
            case "text.classify": return ClipboardLookup.Classify(T(p, "text"), value => _dictionary.Lookup(value) != null);
            case "hover.pick":
                var candidates = Program.Decode<HoverWord[]>(p.GetProperty("candidates"));
                return HoverWords.AtPointer(candidates.Select(value => (Item: value, Word: HoverWords.Normalize(value.Word))).Where(value => value.Word != null).Select(value => value.Item with { Word = value.Word! }), Program.Decode<ScreenPoint>(p.GetProperty("point")));
            case "examples":
                var page = await _examples.SearchAsync(word, T(p, "paperType", "ALL"), Number(p, "offset", 0, 0, 100000), 10, cancellation);
                return new { page.Total, page.Offset, page.ReceivedCount, page.HasMore, items = page.Items.Select(item => new { item.Id, item.English, item.Translation, item.Source, item.OptionLabel, item.ContextEnglish, item.ContextTranslation, item.MatchedWords, translationTerms = TextHighlights.TranslationTerms(item.English, item.Translation, word, item.MatchedWords, value => _dictionary.Lookup(value)?.translation, item.MatchedTranslations, item.TranslationAligned) }) };
            case "notes.local": return DisplayNotes(word, _tokens.CredentialKey);
            case "notes.save":
                var note = Program.Decode<StoredMnemonic>(p.GetProperty("note"));
                _notes.SaveEdit(word, note with { Origin = "local", CloudId = null, AccountKey = null }, _tokens.CredentialKey);
                return DisplayNotes(word, _tokens.CredentialKey);
            case "notes.cloud":
                var vocabulary = await RequireWord(word, cancellation);
                var cloud = await _api.GetNotesAsync(vocabulary.Id, cancellation);
                lock (_accountGate)
                {
                    _api.CheckNativeAccount(accountKey!);
                    _notes.SaveCloud(word, cloud.Select(value => new StoredMnemonic(value.Id, value.NoteType, value.Text, value.Id, "cloud", accountKey)), accountKey!);
                    return DisplayNotes(word, accountKey);
                }
            case "notes.publish":
                var voc = await RequireWord(word, cancellation);
                var saved = await _api.SaveNoteAsync(voc.Id, Null(T(p, "id")), T(p, "noteType"), T(p, "text"), cancellation);
                lock (_accountGate)
                {
                    _api.CheckNativeAccount(accountKey!);
                    _notes.SaveEdit(word, new(saved.Id, saved.NoteType, saved.Text, saved.Id, "cloud", accountKey), accountKey);
                    return DisplayNotes(word, accountKey);
                }
            case "study.status":
                var studyWord = await _api.ResolveWordAsync(word, cancellation);
                return new { status = studyWord == null ? "Missing" : (await _api.GetTodayStudyStatusAsync(studyWord, cancellation)).ToString() };
            case "study.add":
                var verifyOnly = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("verifyOnly", out var verify) && verify.ValueKind == JsonValueKind.True;
                return await _api.AddToTodayAsync(await RequireWord(word, cancellation), verifyOnly, cancellation);
            case "study.todayPlan": return await _api.GetTodayPlanAsync(cancellation);
            case "study.candidates": return await _api.GetTodayCandidatesAsync(Null(T(p, "notepadId")), Null(T(p, "input")), cancellation);
            case "study.addBatch": return await _api.AddTodayBatchAsync(Program.Decode<string[]>(p.GetProperty("ids")), T(p, "day"), cancellation);
            case "stubborn": return await _api.GetStubbornWordsAsync(cancellationToken: cancellation);
            case "notepads.list": return await _api.ListNotepadsAsync(cancellation);
            case "notepads.get": return await _api.GetNotepadAsync(T(p, "id"), cancellation);
            case "notepads.create": return await _api.CreateNotepadAsync(T(p, "title"), Null(word), cancellation);
            case "notepads.add": return await _api.AddToNotepadAsync(T(p, "id"), word, cancellation);
            case "notepads.addWords":
            {
                var padId = T(p, "id");
                var incomingWords = Program.Decode<string[]>(p.GetProperty("words"));
                var currentPad = await _api.GetNotepadAsync(padId, cancellation);
                var existing = new HashSet<string>(NotepadContent.Words(currentPad.Content), StringComparer.OrdinalIgnoreCase);
                var toAdd = new List<string>();
                foreach (var w in incomingWords)
                {
                    if (string.IsNullOrWhiteSpace(w) || existing.Contains(w.Trim())) continue;
                    toAdd.Add(w.Trim());
                    existing.Add(w.Trim());
                }
                if (toAdd.Count == 0) return new { notepad = currentPad, added = 0 };
                var updatedContent = NotepadContent.AddWords(currentPad.Content, toAdd);
                var updatedPad = await _api.UpdateNotepadAsync(currentPad, currentPad.Title, currentPad.Brief, updatedContent, cancellation);
                return new { notepad = updatedPad, added = toAdd.Count };
            }
            case "notepads.addHover":
                return await _api.AddToNotepadAsync(T(p, "id"), (await RequireWord(word, cancellation)).Spelling, cancellation);
            case "notepads.remove": return await _api.RemoveFromNotepadAsync(T(p, "id"), word, cancellation);
            case "notepads.update":
                var original = Program.Decode<CloudNotepad>(p.GetProperty("original"));
                return await _api.UpdateNotepadAsync(original, T(p, "title"), T(p, "brief"), NotepadContent.NormalizeContent(T(p, "content")), cancellation);
            case "notepads.delete": return await _api.DeleteNotepadAsync(Program.Decode<CloudNotepad>(p.GetProperty("original")), cancellation);
            case "notepads.edit":
                var content = T(p, "content"); var words = NotepadContent.ParseWords(T(p, "words"));
                return new { content = T(p, "action") == "remove" ? NotepadContent.RemoveWords(content, words) : NotepadContent.AddWords(content, words) };
            default: throw new ArgumentException("不支持的操作。");
        }
    }
    private object DisplayNotes(string word, string? accountKey) =>
        _notes.Get(word, accountKey).Select(note =>
        {
            var analysis = MnemonicParts.Analyze(word, note.NoteType, note.Text,
                (form, meaning, current) => _notes.FindMorphemeExamples(form, meaning, current));
            var linkSource = analysis.Breakdowns.Count > 0 ? analysis.RemainingText : note.Text;
            var linkableWords = _dictionary.ExtractMnemonicLinkableWords(linkSource, word);
            return new
            {
                note.Id, note.NoteType, note.Text, note.CloudId, note.Origin, note.AccountKey,
                Breakdowns = analysis.Breakdowns,
                RemainingText = analysis.RemainingText,
                LinkableWords = linkableWords
            };
        }).ToArray();

    private async Task<Vocabulary> RequireWord(string word, CancellationToken cancellation) => await _api.ResolveWordAsync(word, cancellation) ?? throw new MaiMemoException("墨墨暂未收录这个单词。");
    private static string T(JsonElement value, string name, string fallback = "") => Program.Text(value, name, fallback);
    private static string? Null(string value) => value.Length == 0 ? null : value;
    private static int Number(JsonElement value, string name, int fallback, int minimum, int maximum) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.TryGetInt32(out var number) ? Math.Clamp(number, minimum, maximum) : fallback;
    public void Dispose() { _api.Dispose(); _examples.Dispose(); _onlineDictionary.Dispose(); _translation.Dispose(); _pronunciation.Dispose(); _related.Dispose(); _chinese.Dispose(); _dictionary.Dispose(); }
}
