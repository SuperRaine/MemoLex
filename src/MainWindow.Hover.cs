using MemoLex.Core;
using MemoLex.Desktop;

namespace MemoLex;
public partial class MainWindow
{
    private HoverLookupController? _hover;
    private void RefreshHoverLookup()
    {
        if(!_desktopIntegration||!_ready)return;
        _hover??=new HoverLookupController(this,_preferences,async(word,cancellation)=>
        {
            var normalized=ExampleText.NormalizeLookup(word);
            var entry=_dictionary.Lookup(normalized);
            if(entry==null&&normalized.EndsWith("'s",StringComparison.OrdinalIgnoreCase))entry=_dictionary.Lookup(normalized[..^2]);
            if(string.IsNullOrWhiteSpace(entry?.translation)&&_preferences.HoverNearest)
            {
                var nearby=(await _related.LookupAsync(normalized,cancellation)).Similar.FirstOrDefault();
                if(nearby!=null)entry=_dictionary.Lookup(nearby.Word);
            }
            return(entry,entry==null?Array.Empty<StoredMnemonic>():_mnemonics.Get(entry.word,MnemonicAccountKey));
        },()=>!_editingSettings&&!_screenTranslating&&IsEnabled,async word=>{RestoreWindow();await LoadWordAsync(word);},word=>_=AutoAddHoverWordAsync(word));
        _hover.Enabled=_preferences.HoverLookupEnabled;
    }
    private readonly HashSet<string> _hoverAdding=new(StringComparer.OrdinalIgnoreCase);
    private async Task AutoAddHoverWordAsync(string word)
    {
        if(!_preferences.HoverAutoAdd||!_allowCloud||!_tokens.HasToken||NotepadCombo.SelectedItem is not Services.CloudNotepad pad)return;
        var account=_accountVersion;var key=account+":"+pad.Id+":"+word;
        if(!_hoverAdding.Add(key))return;
        try
        {
            var cancellation=_accountCts.Token;
            var voc=await _api.ResolveWordAsync(word,cancellation);if(voc==null)return;
            var result=await _api.AddToNotepadAsync(pad.Id,voc.Spelling,cancellation);
            if(account==_accountVersion){ApplyNotepadSnapshot(result.Notepad);StatusText.Text=$"取词「{word}」已在「{pad.Title}」中。";}
        }
        catch(OperationCanceledException){}
        catch(Exception error){_hoverAdding.Remove(key);if(account==_accountVersion)StatusText.Text="取词自动加入未完成："+UserError(error);}
    }
}
