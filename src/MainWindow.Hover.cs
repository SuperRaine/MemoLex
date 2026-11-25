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
        },()=>!_editingSettings&&!_screenTranslating&&IsEnabled,async word=>{RestoreWindow();await LoadWordAsync(word);},
            HoverBookmarkStatus,SaveHoverWordAsync,word=>_=AutoAddHoverWordAsync(word));
        _hover.Enabled=_preferences.HoverLookupEnabled;
    }
    private readonly HashSet<string> _hoverAdding=new(StringComparer.OrdinalIgnoreCase);
    private HoverBookmark HoverBookmarkStatus(string word)
    {
        if(!_allowCloud||!_tokens.HasToken)
            return new(_accountVersion,"","登录墨墨账号后可收藏",false,false);
        if(NotepadCombo.SelectedItem is not Services.CloudNotepad pad||pad.Type=="FAVORITE"||_refreshing)
            return new(_accountVersion,"","请先选择一个云词本",false,false);
        var saved=_notepadCache.TryGetValue(pad.Id,out var cached)&&Services.MaiMemoClient.ContainsWord(cached.Pad,word);
        return new(_accountVersion,pad.Id,pad.Title,saved,_pendingNotepadWords.Contains((pad.Id,word.ToUpperInvariant())));
    }

    private async Task<string?> SaveHoverWordAsync(string word,HoverBookmark target)
    {
        var key=(target.Book,word.ToUpperInvariant());var cancellation=_accountCts.Token;
        bool Current()=>target.Account==_accountVersion&&!cancellation.IsCancellationRequested;
        if(!Current()||!target.Available||!_pendingNotepadWords.Add(key))return null;
        try
        {
            var voc=await _api.ResolveWordAsync(word,cancellation);
            if(voc==null)return "墨墨未收录这个单词";
            var result=await _api.AddToNotepadAsync(target.Book,voc.Spelling,cancellation);
            if(Current())
            {
                ApplyNotepadSnapshot(result.Notepad);
                StatusText.Text=$"「{word}」已在「{target.Title}」中";
            }
            return null;
        }
        catch(OperationCanceledException) when(!Current()){return null;}
        catch(Exception error)
        {
            if(!Current())return null;
            SetAccountError(error);
            return "收藏未完成："+UserError(error);
        }
        finally
        {
            if(Current())
            {
                _pendingNotepadWords.Remove(key);
                if(word.Equals(_word,StringComparison.OrdinalIgnoreCase)&&(NotepadCombo.SelectedItem as Services.CloudNotepad)?.Id==target.Book)
                    _=UpdateMembershipAsync();
            }
        }
    }
    private async Task AutoAddHoverWordAsync(string word)
    {
        if(!_preferences.HoverAutoAdd)return;
        var target=HoverBookmarkStatus(word);
        if(!target.Available||target.Pending||target.Saved)return;
        var key=target.Account+":"+target.Book+":"+word;
        if(!_hoverAdding.Add(key))return;
        if(await SaveHoverWordAsync(word,target) is { } error)
        {
            _hoverAdding.Remove(key);
            if(target.Account==_accountVersion)StatusText.Text=error;
        }
    }
}
