using System.Runtime.InteropServices;
using System.Windows;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;
public partial class MainWindow
{
    private DoubleCopyMonitor? _doubleCopy;
    private CancellationTokenSource? _clipboardCts;
    private void RefreshDoubleCopy()
    {
        _doubleCopy?.Dispose();_doubleCopy=null;
        _clipboardCts?.Cancel();
        if(!_desktopIntegration||!_preferences.DoubleCopyEnabled)return;
        try{_doubleCopy=new DoubleCopyMonitor(signal=>Dispatcher.BeginInvoke(new Action(()=>DoubleCopyAsync(signal))));}
        catch{StatusText.Text="连按两次 Ctrl+C 暂时不可用，请使用剪贴板查词快捷键。";}
    }
    private async void DoubleCopyAsync(DoubleCopySignal signal)
    {
        if(_editingSettings||_screenTranslating||!_ready||OwnedWindows.Cast<Window>().Any(window=>window.IsVisible))return;
        DoubleCopyMonitor.GetWindowThreadProcessId(signal.Foreground,out var process);
        if(process==Environment.ProcessId)return;
        _clipboardCts?.Cancel();_clipboardCts?.Dispose();_clipboardCts=new();var cancellation=_clipboardCts.Token;
        try
        {
            // Let the source application handle both normal copy events first.
            for(var attempt=0;attempt<25;attempt++)
            {
                await Task.Delay(40,cancellation);
                if(DoubleCopyMonitor.GetForegroundWindow()!=signal.Foreground)return;
                if(DoubleCopyMonitor.GetClipboardSequenceNumber()==signal.InitialSequence)continue;
                try
                {
                    if(!Clipboard.ContainsText())continue;
                    var text=Clipboard.GetText();
                    if(ClipboardLookup.Classify(text,word=>_dictionary.Lookup(word)!=null).Kind==ClipboardLookupKind.Ignore)return;
                    RestoreWindow();await ProcessClipboardTextAsync(text);return;
                }
                catch(ExternalException){ }
            }
        }
        catch(OperationCanceledException){ }
    }
    internal async Task ProcessClipboardTextAsync(string text)
    {
        if(!_ready)return;
        var request=ClipboardLookup.Classify(text,word=>_dictionary.Lookup(word)!=null);
        switch(request.Kind)
        {
            case ClipboardLookupKind.Word:await LoadWordAsync(request.Text);break;
            case ClipboardLookupKind.Chinese:
                await SearchChineseAsync(request.Text);
                if(_viewTarget==ViewTarget.ChineseSearch&&_chineseQuery==request.Text&&_chineseReady&&_chineseTotal==0)await TranslateClipboardAsync(request.Text,"zh-CN|en");
                break;
            case ClipboardLookupKind.Paragraph:await TranslateClipboardAsync(request.Text,request.Direction);break;
            default:StatusText.Text="剪贴板中没有可查询的文字。";break;
        }
    }
    private async Task TranslateClipboardAsync(string text,string direction)
    {
        CancelTranslation();TranslationModeButton_Click(this,new RoutedEventArgs());
        TranslationInput.Text=text;TranslationDirectionCombo.SelectedIndex=direction=="zh-CN|en"?1:0;
        await TranslateParagraphAsync();
    }
}
