using System.Windows;
using System.Windows.Controls;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;
public partial class MainWindow
{
    private readonly MnemonicStore _mnemonics;
    private CancellationTokenSource? _notesCts;
    private int _notesVersion;
    private StoredMnemonic[] _displayedNotes=[];
    private string? MnemonicAccountKey=>_accountCredentialKey!=null&&_accountCredentialKey==_tokens.CredentialKey&&_tokens.ActiveAccount is {} account?account.Id+":"+_accountCredentialKey:null;
    private void RenderNotes(string word,IReadOnlyList<StoredMnemonic> notes,bool cloud=false)
    {
        if(word!=_word)return;
        _displayedNotes=notes.Select(note=>note with {NoteType=MnemonicTypes.Normalize(note.NoteType)}).ToArray();NotesPanel.Children.Clear();EditNotesButton.IsEnabled=ValidWord(word);
        foreach(var note in _displayedNotes)
        {
            var stack=new StackPanel();var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var origin=note.Origin=="cloud"?(cloud?"云端助记":"云端助记（离线）"):"本地助记";
            header.Children.Add(new TextBlock {Text=note.NoteType+" · "+origin,FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Brush("#1688B7"),VerticalAlignment=VerticalAlignment.Center});
            var edit=new Button {Content="编辑",FontSize=11,Padding=new Thickness(7,4,7,4),Background=System.Windows.Media.Brushes.Transparent,BorderThickness=new Thickness(0)};
            edit.Click+=(_,_)=>OpenMnemonicEditor(note);Grid.SetColumn(edit,1);header.Children.Add(edit);stack.Children.Add(header);
            var text=BuildSentenceText(note.Text,EmptyMatches,word,15);text.FontFamily=FontFamily;text.Margin=new Thickness(0,6,0,0);stack.Children.Add(text);
            NotesPanel.Children.Add(new Border {Child=stack,Padding=new Thickness(15),Margin=new Thickness(0,8,0,0),Background=Brush("#F3F9FE"),CornerRadius=new CornerRadius(8)});
        }
    }
    private string LocalNotesHint()=>_displayedNotes.Length>0?$"本地助记 · {_displayedNotes.Length} 条":"暂无助记，点击「编辑助记」添加。";
    private async Task LoadLocalAndCloudNotesAsync(string word,CancellationToken cancellation)
    {
        _notesCts?.Cancel();_notesCts?.Dispose();_notesCts=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token=_notesCts.Token;var version=++_notesVersion;
        var accountVersion=_accountVersion;var credential=_accountCredentialKey;
        if(credential!=_tokens.CredentialKey){CheckAccountExpiry();return;}
        bool Current()=>!token.IsCancellationRequested&&version==_notesVersion&&word==_word&&accountVersion==_accountVersion&&credential==_tokens.CredentialKey;
        var cacheKey=(credential??"offline")+":"+word.ToLowerInvariant();
        var accountKey=MnemonicAccountKey;
        RenderNotes(word,_mnemonics.Get(word,accountKey));NotesHintText.Text=LocalNotesHint();
        if(!_allowCloud||!_tokens.HasToken)
        {
            SetAccountStatus(!_allowCloud?"离线模式":InactiveAccountStatus);
            StatusText.Text="查词完成。";
            if(_mnemonics.LoadWarning!=null)NotesHintText.Text+=" · "+_mnemonics.LoadWarning;
            return;
        }
        NotesHintText.Text="正在加载云端助记…";
        var studyStarted=false;var studyTask=Task.CompletedTask;
        try
        {
            Vocabulary? voc;IReadOnlyList<MemoNote> notes;
            if(_cloudCache.TryGetValue(cacheKey,out var cached)&&DateTime.UtcNow-cached.Time<TimeSpan.FromMinutes(5)){voc=cached.Voc;notes=cached.Notes;}
            else
            {
                voc=await _api.ResolveWordAsync(word,token);token.ThrowIfCancellationRequested();
                if(!Current())return;
                _vocabulary=voc;studyTask=LoadTodayStudyStatusAsync(word,voc);studyStarted=true;
                notes=voc==null?[]:await _api.GetNotesAsync(voc.Id,token);
                token.ThrowIfCancellationRequested();if(!Current())return;_cloudCache[cacheKey]=(DateTime.UtcNow,voc,notes);
            }
            if(!Current())return;
            _vocabulary=voc;if(!studyStarted){studyTask=LoadTodayStudyStatusAsync(word,voc);studyStarted=true;}SetAccountStatus("已连接");
            var usable=notes.Where(note=>!string.IsNullOrWhiteSpace(note.Text)).Select(note=>new StoredMnemonic("cloud:"+note.Id,note.NoteType,note.Text,note.Id,"cloud",accountKey)).ToArray();
            if(usable.Length>0)
            {
                RenderNotes(word,usable,true);NotesHintText.Text=$"云端助记 · {usable.Length} 条";
                try{if(accountKey!=null)_mnemonics.SaveCloud(word,usable,accountKey);}
                catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException){NotesHintText.Text+=" · 未能保存到本机";}
            }
            else
            {
                if(accountKey!=null)_mnemonics.SaveCloud(word,[],accountKey);
                RenderNotes(word,_mnemonics.Get(word,accountKey));NotesHintText.Text=LocalNotesHint();
            }
            StatusText.Text="查词完成";
        }
        catch(OperationCanceledException){ }
        catch(Exception error)
        {
            if(!Current())return;
            NotesHintText.Text=LocalNotesHint()+" · "+UserError(error);SetAccountError(error);
            if(!studyStarted)FailTodayStudyLookup(word,error);
        }
        finally{await studyTask;}
    }
    private void EditNotesButton_Click(object sender,RoutedEventArgs e)=>OpenMnemonicEditor(_displayedNotes.FirstOrDefault());
    private void OpenMnemonicEditor(StoredMnemonic? original)
    {
        CreateMnemonicEditor(original)?.ShowDialog();
    }
    internal Window? CreateMnemonicEditor(StoredMnemonic? original)
    {
        if(!_ready||!ValidWord(_word))return null;
        var word=_word;var account=_accountVersion;var accountKey=MnemonicAccountKey;var note=original??new StoredMnemonic("local:"+Guid.NewGuid().ToString("N"),"词根词缀","");
        ++_notesVersion;_notesCts?.Cancel();NotesHintText.Text=LocalNotesHint();
        return MnemonicEditor.Build(this,word,note.NoteType,note.Text,_allowCloud&&_tokens.HasToken,async(type,text,sync)=>
        {
            var edited=note with {NoteType=type,Text=text,Origin="user"};
            void Persist(StoredMnemonic value)
            {
                var current=_mnemonics.Get(word,accountKey).ToList();var index=current.FindIndex(item=>item.Id==note.Id);
                if(index<0)current.Add(value);else current[index]=value;
                _mnemonics.SaveEdit(word,value,accountKey);_cloudCache.Remove((_accountCredentialKey??"offline")+":"+word.ToLowerInvariant());
                if(word==_word){RenderNotes(word,current,value.Origin=="cloud");NotesHintText.Text=value.Origin=="cloud"?"已同步到墨墨。":"已保存到本机。";}
            }
            Persist(edited);
            if(!sync)return;
            if(account!=_accountVersion)throw new MaiMemoException("账号已变更，本地已保存，请重新打开助记后同步。");
            try
            {
                var voc=_vocabulary?.Spelling.Equals(word,StringComparison.OrdinalIgnoreCase)==true?_vocabulary:await _api.ResolveWordAsync(word);
                if(voc==null)throw new MaiMemoException("墨墨尚未收录这个单词。");
                var saved=await _api.SaveNoteAsync(voc.Id,note.AccountKey==accountKey?note.CloudId:null,type,text,_accountCts.Token);
                edited=edited with {CloudId=saved.Id,NoteType=saved.NoteType,Text=saved.Text,Origin="cloud",AccountKey=accountKey};
                note=edited;Persist(edited);
            }
            catch(Exception error){SetAccountError(error);throw new MaiMemoException("已保存到本地，未能同步到墨墨："+UserError(error));}
        });
    }
}
