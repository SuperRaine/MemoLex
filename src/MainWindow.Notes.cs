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
    private void RenderNotes(string word,IReadOnlyList<StoredMnemonic> notes,bool cloud=false)
    {
        if(word!=_word)return;
        _displayedNotes=notes.ToArray();NotesPanel.Children.Clear();EditNotesButton.IsEnabled=ValidWord(word);
        foreach(var note in notes)
        {
            var stack=new StackPanel();var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var origin=note.Origin=="cloud"?(cloud?"云端助记":"本地缓存"):"本地助记";
            header.Children.Add(new TextBlock {Text=note.NoteType+" · "+origin,FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Brush("#1688B7"),VerticalAlignment=VerticalAlignment.Center});
            var edit=new Button {Content="编辑",FontSize=11,Padding=new Thickness(7,4,7,4),Background=System.Windows.Media.Brushes.Transparent,BorderThickness=new Thickness(0)};
            edit.Click+=(_,_)=>OpenMnemonicEditor(note);Grid.SetColumn(edit,1);header.Children.Add(edit);stack.Children.Add(header);
            var text=BuildSentenceText(note.Text,EmptyMatches,word,15);text.FontFamily=FontFamily;text.Margin=new Thickness(0,6,0,0);stack.Children.Add(text);
            NotesPanel.Children.Add(new Border {Child=stack,Padding=new Thickness(15),Margin=new Thickness(0,8,0,0),Background=Brush("#F3F9FE"),CornerRadius=new CornerRadius(8)});
        }
    }
    private string LocalNotesHint()=>_displayedNotes.Length>0?$"本地助记 · {_displayedNotes.Length} 条，支持编辑":"暂无本地助记，点击「编辑助记」创建。";
    private async Task LoadLocalAndCloudNotesAsync(string word,CancellationToken cancellation)
    {
        _notesCts?.Cancel();_notesCts?.Dispose();_notesCts=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token=_notesCts.Token;var version=++_notesVersion;
        RenderNotes(word,_mnemonics.Get(word));NotesHintText.Text=LocalNotesHint();
        if(!_allowCloud||!_tokens.HasToken)
        {
            SetAccountStatus(!_allowCloud?"离线模式":"未连接");
            StatusText.Text="离线释义与本地助记已就绪。";
            if(_mnemonics.LoadWarning!=null)NotesHintText.Text+=" · "+_mnemonics.LoadWarning;
            return;
        }
        NotesHintText.Text="正在核对云端助记 · 已先显示本地内容…";
        try
        {
            Vocabulary? voc;IReadOnlyList<MemoNote> notes;
            if(_cloudCache.TryGetValue(word,out var cached)&&DateTime.UtcNow-cached.Time<TimeSpan.FromMinutes(5)){voc=cached.Voc;notes=cached.Notes;}
            else
            {
                voc=await _api.ResolveWordAsync(word,token);notes=voc==null?[]:await _api.GetNotesAsync(voc.Id,token);
                token.ThrowIfCancellationRequested();_cloudCache[word]=(DateTime.UtcNow,voc,notes);
            }
            if(token.IsCancellationRequested||version!=_notesVersion||word!=_word)return;
            _vocabulary=voc;SetAccountStatus("已连接");
            var usable=notes.Where(note=>!string.IsNullOrWhiteSpace(note.Text)).Select(note=>new StoredMnemonic("cloud:"+note.Id,note.NoteType,note.Text,note.Id,"cloud")).ToArray();
            if(usable.Length>0)
            {
                RenderNotes(word,usable,true);NotesHintText.Text=$"云端助记 · {usable.Length} 条，已覆盖本地显示";
                try{_mnemonics.Save(word,usable);NotesHintText.Text+="并缓存";}
                catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException){NotesHintText.Text+="；本地缓存保存失败";}
            }
            else NotesHintText.Text=LocalNotesHint()+" · 云端暂无助记";
            StatusText.Text="查询完成 · 助记可在本地编辑";
        }
        catch(OperationCanceledException){ }
        catch(Exception error)
        {
            if(token.IsCancellationRequested||version!=_notesVersion||word!=_word)return;
            NotesHintText.Text=LocalNotesHint()+" · "+UserError(error);SetAccountError(error);
        }
    }
    private void EditNotesButton_Click(object sender,RoutedEventArgs e)=>OpenMnemonicEditor(_displayedNotes.FirstOrDefault());
    private void OpenMnemonicEditor(StoredMnemonic? original)
    {
        CreateMnemonicEditor(original)?.ShowDialog();
    }
    internal Window? CreateMnemonicEditor(StoredMnemonic? original)
    {
        if(!_ready||!ValidWord(_word))return null;
        var word=_word;var note=original??new StoredMnemonic("local:"+Guid.NewGuid().ToString("N"),"词根词缀","");
        ++_notesVersion;_notesCts?.Cancel();NotesHintText.Text=LocalNotesHint();
        return MnemonicEditor.Build(this,word,note.NoteType,note.Text,_allowCloud&&_tokens.HasToken,async(type,text,sync)=>
        {
            var edited=note with {NoteType=type,Text=text,Origin="local"};
            void Persist(StoredMnemonic value)
            {
                var current=_mnemonics.Get(word).ToList();var index=current.FindIndex(item=>item.Id==note.Id);
                if(index<0)current.Add(value);else current[index]=value;
                _mnemonics.Save(word,current);_cloudCache.Remove(word);
                if(word==_word){RenderNotes(word,current,value.Origin=="cloud");NotesHintText.Text=value.Origin=="cloud"?"已同步到墨墨，并保存在本地。":"已保存到本地 · 下次查到云端助记时以云端为准。";}
            }
            Persist(edited);
            if(!sync)return;
            try
            {
                var voc=_vocabulary?.Spelling.Equals(word,StringComparison.OrdinalIgnoreCase)==true?_vocabulary:await _api.ResolveWordAsync(word);
                if(voc==null)throw new MaiMemoException("墨墨尚未收录这个单词。");
                var saved=await _api.SaveNoteAsync(voc.Id,note.CloudId,type,text);
                edited=edited with {CloudId=saved.Id,NoteType=saved.NoteType,Text=saved.Text,Origin="cloud"};
                note=edited;Persist(edited);
            }
            catch(Exception error){SetAccountError(error);throw new MaiMemoException("本地已保存，云端同步未完成："+UserError(error));}
        });
    }
}
