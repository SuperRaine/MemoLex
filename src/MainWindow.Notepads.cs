using System.Windows;
using System.Windows.Controls;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private readonly Dictionary<string,(DateTime At,CloudNotepad Pad)> _notepadCache=new();
    private readonly Dictionary<string,Task<CloudNotepad>> _notepadLoads=new();
    private int _membershipVersion;
    private string _membershipWord="",_membershipPad="";
    private bool _isMember;
    private readonly HashSet<(string Pad, string Word)> _pendingNotepadWords = new();
    private readonly HashSet<string> _deletedNotepads=new();
    internal Func<Window,CloudNotepad,int,bool> ConfirmNotepadDeletion {get;set;}=NotepadDeletionDialog.Confirm;
    private void RemoveNotepadSnapshot(string id)
    {
        _deletedNotepads.Add(id);_notepadCache.Remove(id);++_membershipVersion;_membershipWord=_membershipPad="";
        var pads=NotepadCombo.Items.Cast<CloudNotepad>().Where(pad=>pad.Id!=id).ToArray();
        _changingLists=true;
        try{NotepadCombo.ItemsSource=pads;NotepadCombo.SelectedItem=pads.FirstOrDefault();}
        finally{_changingLists=false;}
        _preferences.NotepadId=(NotepadCombo.SelectedItem as CloudNotepad)?.Id;_preferences.Save();
        CloudDetailText.Text=pads.Length==0?"还没有云词本，点击新建":$"{pads.Length} 个云词本";
        StatusText.Text="云词本已删除。";
    }
    private void CacheNotepad(CloudNotepad pad)=>_notepadCache[pad.Id]=(DateTime.UtcNow,pad);
    private void ApplyNotepadSnapshot(CloudNotepad updated)
    {
        CacheNotepad(updated);
        var selected=(NotepadCombo.SelectedItem as CloudNotepad)?.Id;
        var pads=NotepadCombo.Items.Cast<CloudNotepad>().Select(pad=>pad.Id==updated.Id?updated:pad).ToArray();
        _changingLists=true;
        try{NotepadCombo.ItemsSource=pads;NotepadCombo.SelectedItem=pads.FirstOrDefault(pad=>pad.Id==selected);}
        finally{_changingLists=false;}
    }
    private async Task<CloudNotepad> ReadNotepadAsync(string id)
    {
        if(_notepadLoads.TryGetValue(id,out var pending))return await pending;
        var account=_accountVersion;var started=DateTime.UtcNow;var request=_api.GetNotepadAsync(id,_accountCts.Token);_notepadLoads[id]=request;
        try
        {
            var pad=await request;
            if(account!=_accountVersion)throw new OperationCanceledException();
            if(_deletedNotepads.Contains(id))throw new MaiMemoException("这个云词本已删除，请选择其他词本。");
            if(_notepadCache.TryGetValue(id,out var newer)&&newer.At>started)return newer.Pad;
            CacheNotepad(pad);return pad;
        }
        finally{if(_notepadLoads.GetValueOrDefault(id)==request)_notepadLoads.Remove(id);}
    }
    private async Task UpdateMembershipAsync(bool force=false)
    {
        var version=++_membershipVersion;var word=_word;var pad=NotepadCombo.SelectedItem as CloudNotepad;
        _membershipWord=_membershipPad="";
        if(!_ready||!_allowCloud||!_tokens.HasToken) {AddButton.IsEnabled=_allowCloud&&!_adding;return;}
        if(pad==null){AddButton.Content="＋ 新建词本并加入";AddButton.IsEnabled=!_adding&&!_refreshing;EditNotepadButton.IsEnabled=false;return;}
        EditNotepadButton.IsEnabled = !_adding && !_refreshing;
        if (_pendingNotepadWords.Contains((pad.Id, word.ToUpperInvariant())))
        {
            AddButton.Content = "正在保存…";
            AddButton.IsEnabled = false;
            return;
        }
        AddButton.Content="正在加载词本…";AddButton.IsEnabled=false;
        try
        {
            CloudNotepad full;
            if(!force&&_notepadCache.TryGetValue(pad.Id,out var cached)&&DateTime.UtcNow-cached.At<TimeSpan.FromMinutes(3))full=cached.Pad;
            else full=await ReadNotepadAsync(pad.Id);
            if(version!=_membershipVersion||word!=_word)return;
            _membershipWord=word;_membershipPad=pad.Id;_isMember=MaiMemoClient.ContainsWord(full,word);
            AddButton.Content=_isMember?"✓ 已收藏 · 点击移除":"＋ 加入云词本";
            AddButton.ToolTip=(_isMember?"从当前云词本移除这个单词":"加入当前云词本")+"（"+ShortcutLabel("toggleWord")+"）";
            if(_isMember&&Core.NotepadContent.Words(full.Content).Count==1)AddButton.ToolTip="这是词本中的最后一个单词，移除时会询问是否删除整个词本。";
            AddButton.IsEnabled=!_adding&&!_refreshing&&ValidWord(word);
            UpdateRepeatedWordBanner();
        }
        catch(Exception error)
        {
            if(version!=_membershipVersion)return;
            AddButton.Content="重新加载词本";AddButton.IsEnabled=!_adding&&!_refreshing;
            StatusText.Text=UserError(error);
        }
    }
    private async void EditNotepadButton_Click(object sender,RoutedEventArgs e)
    {
        if(_adding||_refreshing||NotepadCombo.SelectedItem is not CloudNotepad selected)return;
        _adding=true;SetCloudWriteBusy(true);AddButton.IsEnabled=false;
        try
        {
            var full=await _api.GetNotepadAsync(selected.Id);CacheNotepad(full);
            NotepadEditor.Build(this,full,_api,updated=>
            {
                ApplyNotepadSnapshot(updated);
                StatusText.Text="云词本修改已保存。";
            },_dictionary,word=>LoadWordAsync(word),deleted:RemoveNotepadSnapshot,confirmDeletion:ConfirmNotepadDeletion,online:_onlineDictionary,
                startFlashcards:(ownerDialog,words,persist)=>
                {
                    var scopeKey="notepad:"+full.Id;
                    var saved=persist?FlashcardProgressStore.Load(_tokens.ProfileDirectory,scopeKey):null;
                    var practice=Desktop.StubbornFlashcards.Build(ownerDialog,words,_dictionary,_onlineDictionary,
                        word=>_mnemonics.Get(word,MnemonicAccountKey).FirstOrDefault()?.Text,
                        savedProgress:saved,
                        onProgressChanged:snapshot=>FlashcardProgressStore.Save(_tokens.ProfileDirectory,scopeKey,snapshot),
                        onResetProgress:()=>FlashcardProgressStore.Remove(_tokens.ProfileDirectory,scopeKey),
                        persistProgress:persist,
                        title:$"{full.Title} · 卡片自测",
                        speak:(w,accent)=>PlaySpeechAsync(w,accent),
                        getMnemonics:w=>_mnemonics.Get(w,MnemonicAccountKey),
                        mnemonicStore:_mnemonics,
                        preferences:_preferences,
                        spellingMistakes:_spellingMistakes,
                        contextSentences:_contextSentences,
                        accountKey:MnemonicAccountKey);
                    practice.ShowDialog();
                }).ShowDialog();
        }
        catch(Exception error){ShowError(error);}
        finally{_adding=false;SetCloudWriteBusy(false);await UpdateMembershipAsync();}
    }
}
