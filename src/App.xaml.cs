using System.IO;
using System.Text.Json;
using System.Windows;
using System.Runtime.InteropServices;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;
public partial class App : Application
{
    private Mutex? _instance;
    internal static readonly int ActivateMessage = RegisterWindowMessage("MemoLex.Activate.v1");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--smoke-test"))
        {
            var result = new Dictionary<string, object?>();
            try
            {
                using var dictionary = new LocalDictionary();
                await dictionary.LoadAsync();
                result["wordCount"] = dictionary.Count;
                result["apple"] = dictionary.Lookup("APPLE")?.translation;
                result["serendipity"] = dictionary.Lookup("serendipity")?.translation;
                result["suggestions"] = dictionary.Suggest("serend", 8);
                result["missing"] = dictionary.Lookup("zzzzzzmemo_nonexistent") == null;
                using var related=new RelatedWords(dictionary);
                await related.LoadAsync();
                var relations=await related.LookupAsync("talkative");
                result["wordnetAvailable"]=related.Available;
                result["synonymCheck"]=relations.Synonyms.Any(item=>item.Word=="loquacious");
                var phraseRelations=await related.LookupAsync("take");
                result["relatedPhraseCount"]=phraseRelations.Phrases.Count;
                result["relatedPhraseCheck"]=phraseRelations.Phrases.Any(item=>item.Word=="take off"&&item.Meaning.Contains("起飞"));
                using var chinese=new ChineseReverseSearch(dictionary);await chinese.LoadAsync();
                var reverse=await chinese.SearchAsync("快乐",120);
                result["chineseReverseCheck"]=reverse.Items.Take(10).Any(item=>item.Word=="happy")&&reverse.Items.Any(item=>item.Word=="cheerful");
                var mnemonics=new MnemonicStore(Path.Combine(Path.GetTempPath(),"MemoLex-smoke-"+Guid.NewGuid().ToString("N"),"unused.json"));await mnemonics.LoadAsync();
                result["localMnemonicCount"]=mnemonics.BundledCount;
                result["localMnemonicCheck"]=mnemonics.BundledCount>=10613
                    &&mnemonics.Get("taste").Any(note=>note.Text.Contains("test")&&note.Text.Contains("tasty"))
                    &&mnemonics.Get("bitter").Any(note=>note.Text.Contains("逼他"))
                    &&mnemonics.Get("insight").Any(note=>note.Text.Contains("sight")&&note.Text.Contains("洞察力"));
                if (e.Args.Contains("--online"))
                {
                    var tokenStore = new TokenStore();
                    if (!tokenStore.HasToken) throw new InvalidOperationException("Online smoke test needs an account.");
                    using var api = new MaiMemoClient(tokenStore);
                    var word = await api.ResolveWordAsync("serendipity");
                    result["apiWord"] = word?.Spelling;
                    result["notepadCount"] = (await api.ListNotepadsAsync()).Count;
                    result["noteCount"] = word == null ? null : (await api.GetNotesAsync(word.Id)).Count;
                }
                result["ok"] = dictionary.Count > 1000 && result["apple"] != null && (bool)result["missing"]! && (bool)result["synonymCheck"]! && (bool)result["relatedPhraseCheck"]! && (bool)result["chineseReverseCheck"]!&&(bool)result["localMnemonicCheck"]!;
            }
            catch (Exception ex) { result["ok"] = false; result["errorType"] = ex.GetType().Name; }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(result.GetValueOrDefault("ok") is true ? 0 : 1);
            return;
        }
        _instance = new Mutex(true, "Local\\MemoLex.Desktop.v1", out var firstInstance);
        if (!firstInstance)
        {
            PostMessage(new IntPtr(0xFFFF), ActivateMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }
        Exit += (_, _) => { _instance.ReleaseMutex(); _instance.Dispose(); };
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("操作未完成，请重试。若网络不可用，仍可使用离线词典。", "墨墨搜词", MessageBoxButton.OK, MessageBoxImage.Information);
            args.Handled = true;
        };
        MainWindow = new MainWindow(allowCloud: !e.Args.Contains("--offline"));
        MainWindow.Show();
    }
}
