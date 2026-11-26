using System.Windows;
using System.Windows.Controls;
using MemoLex.Core;
using MemoLex.Desktop;

namespace MemoLex;

public partial class MainWindow
{
    private sealed record TranslationSnapshot(string Input,string Direction,string Output,string Source,double ScrollOffset,string Provider);
    private CancellationTokenSource? _translationCts;
    private int _translationVersion;
    private string _translatedText="",_translatedSource="";
    private string _translatedProvider="";
    private string TranslationPair => (TranslationDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string??"en|zh-CN";
    private TranslationSnapshot CaptureTranslation()=>new(TranslationInput.Text,TranslationPair,_translatedText,_translatedSource,TranslationScroll.VerticalOffset,_translatedProvider);
    private void TranslationScroll_ScrollChanged(object sender,ScrollChangedEventArgs e)
    {
        if(e.VerticalChange!=0&&ReferenceEquals(e.Source,TranslationScroll)&&_viewTarget==ViewTarget.Translation)RememberCurrentView();
    }
    private void CancelTranslation()
    {
        ++_translationVersion;_translationCts?.Cancel();_translationCts?.Dispose();_translationCts=null;
        if(TranslateButton!=null){TranslateButton.IsEnabled=true;TranslateButton.Content="翻译";}
    }
    private void EnterTranslationView()
    {
        StopPronunciation();
        CancelChineseSearch();ChineseSearchView.Visibility=Visibility.Collapsed;
        _lookupCts?.Cancel();_examplesCts?.Cancel();++_wordVersion;++_membershipVersion;
        _viewTarget=ViewTarget.Translation;ResultScroll.Visibility=Visibility.Collapsed;TranslationView.Visibility=Visibility.Visible;
        UiMotion.Reveal(TranslationView);
        UpdateHeader();
    }
    private void TranslationModeButton_Click(object sender,RoutedEventArgs e)
    {
        if(_viewTarget==ViewTarget.Translation){TranslationInput.Focus();return;}
        RememberCurrentView();_navigating=true;EnterTranslationView();_navigating=false;
        _navigation.Navigate(CaptureView());UpdateNavigationButtons();TranslationInput.Focus();
    }
    private void RestoreTranslationView(ViewState state)
    {
        CancelTranslation();_navigating=true;EnterTranslationView();
        if(state.Translation is { } snapshot)
        {
            TranslationInput.Text=snapshot.Input;
            TranslationDirectionCombo.SelectedIndex=snapshot.Direction=="zh-CN|en"?1:0;
            _translatedText=snapshot.Output;_translatedSource=snapshot.Source;_translatedProvider=snapshot.Provider;
            RenderTranslationText();
            TranslationHintText.Text="点击英文单词查看释义。";
            QueueViewRestore(_wordVersion,()=>{TranslationScroll.UpdateLayout();TranslationScroll.ScrollToVerticalOffset(snapshot.ScrollOffset);});
        }
        _navigating=false;UpdateNavigationButtons();
    }
    private async Task NavigateTranslationWordAsync(string word)
    {
        word=ExampleText.ResolveLookupWord(word,spelling=>_dictionary.Lookup(spelling)!=null);
        if(ValidWord(word))await LoadWordAsync(word);
    }
    private readonly List<ExtractedVocabularyItem> _extractedWords=[];
    private readonly HashSet<string> _selectedExtracted=new(StringComparer.OrdinalIgnoreCase);
    private void RenderTranslationText()
    {
        FillWordLinks(TranslationSourceText,_translatedSource.Length>0?_translatedSource:"在上方输入或粘贴原文。",EmptyMatches,_word,navigate:NavigateTranslationWordAsync);
        FillWordLinks(TranslationOutputText,_translatedText,EmptyMatches,_word,navigate:NavigateTranslationWordAsync);
        TranslationOutputText.ToolTip = _translatedText.Length > 0 && _translatedProvider.Length > 0 ? "翻译来源 " + _translatedProvider : null;
        UpdateExtractedVocabulary();
    }
    private void UpdateExtractedVocabulary()
    {
        _extractedWords.Clear();
        _selectedExtracted.Clear();
        TranslationExtractedPanel.Children.Clear();
        TranslationExtractedStatus.Visibility=Visibility.Collapsed;
        TranslationExtractedStatus.Text="";

        var englishCorpus=TranslationPair=="zh-CN|en"?_translatedText:_translatedSource;
        if(string.IsNullOrWhiteSpace(englishCorpus))
        {
            TranslationExtractedCard.Visibility=Visibility.Collapsed;
            return;
        }
        var items=VocabularyExtractor.Extract(englishCorpus,_dictionary,24);
        if(items.Count==0)
        {
            TranslationExtractedCard.Visibility=Visibility.Collapsed;
            return;
        }
        _extractedWords.AddRange(items);
        foreach(var item in items)_selectedExtracted.Add(item.Word);
        RenderExtractedChips();
        TranslationExtractedCard.Visibility=Visibility.Visible;
        UiMotion.Reveal(TranslationExtractedCard,distance:5);
    }
    private void RenderExtractedChips()
    {
        TranslationExtractedPanel.Children.Clear();
        TranslationExtractedTitle.Text=$"生词一键提取 · 识别到 {_extractedWords.Count} 个重点词（已选 {_selectedExtracted.Count}）";
        BatchAddExtractedButton.IsEnabled=_selectedExtracted.Count>0;
        SelectAllExtractedButton.Content=_selectedExtracted.Count==_extractedWords.Count?"取消全选":"全选";

        foreach(var item in _extractedWords)
        {
            var word=item.Word;
            var chosen=_selectedExtracted.Contains(word);
            var chipPanel=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var checkMark=new TextBlock{Text=chosen?"✓ ":"＋ ",FontSize=11.5,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};
            checkMark.SetResourceReference(TextBlock.ForegroundProperty,chosen?"AccentDeepBrush":"MutedBrush");
            var wordLbl=new TextBlock{Text=word,FontSize=12.5,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
            wordLbl.SetResourceReference(TextBlock.ForegroundProperty,chosen?"InkStrongBrush":"MutedBrush");
            chipPanel.Children.Add(checkMark);
            chipPanel.Children.Add(wordLbl);
            if(item.Tags.Count>0)
            {
                var tagLbl=new TextBlock{Text=item.Tags[0],FontSize=10,Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center};
                tagLbl.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush");
                chipPanel.Children.Add(tagLbl);
            }
            var chip=new Button
            {
                Content=chipPanel,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,7,7),
                ToolTip=$"{item.Word}{(item.Phonetic.Length>0?$" /{item.Phonetic}/":"")}\n{item.Meaning}\n（单击选中/取消，右键查词）"
            };
            chip.SetResourceReference(Control.BackgroundProperty,chosen?"SelectedBgBrush":"ControlBgBrush");
            chip.SetResourceReference(Control.BorderBrushProperty,chosen?"AccentBrush":"BorderBrush");
            chip.Click+=(_,_)=>
            {
                if(!_selectedExtracted.Remove(word))_selectedExtracted.Add(word);
                RenderExtractedChips();
            };
            chip.MouseRightButtonUp+=async(_,e)=>{e.Handled=true;await LoadWordAsync(word);};
            TranslationExtractedPanel.Children.Add(chip);
        }
    }
    private void SelectAllExtractedButton_Click(object sender,RoutedEventArgs e)
    {
        if(_selectedExtracted.Count==_extractedWords.Count)_selectedExtracted.Clear();
        else foreach(var item in _extractedWords)_selectedExtracted.Add(item.Word);
        RenderExtractedChips();
    }
    private async void BatchAddExtractedButton_Click(object sender,RoutedEventArgs e)
    {
        if(_selectedExtracted.Count==0)return;
        if(!_allowCloud||!_tokens.HasToken){AccountButton_Click(sender,e);return;}
        if(NotepadCombo.SelectedItem is not Services.CloudNotepad pad)
        {
            TranslationExtractedStatus.Text="请先在左侧选择或新建一个云词本。";
            TranslationExtractedStatus.Visibility=Visibility.Visible;
            return;
        }
        var wordsToAdd=_selectedExtracted.ToArray();
        BatchAddExtractedButton.IsEnabled=false;
        TranslationExtractedStatus.Text=$"正在将 {wordsToAdd.Length} 个单词加入「{pad.Title}」…";
        TranslationExtractedStatus.Visibility=Visibility.Visible;
        try
        {
            var full=await ReadNotepadAsync(pad.Id);
            var existing=new HashSet<string>(NotepadContent.Words(full.Content),StringComparer.OrdinalIgnoreCase);
            var fresh=wordsToAdd.Where(w=>!existing.Contains(w)).ToArray();
            if(fresh.Length==0)
            {
                TranslationExtractedStatus.Text=$"所选 {wordsToAdd.Length} 个单词均已在「{pad.Title}」中。";
                return;
            }
            var updatedContent=NotepadContent.AddWords(full.Content,fresh);
            var saved=await _api.UpdateNotepadAsync(full,full.Title,full.Brief,updatedContent,_accountCts.Token);
            ApplyNotepadSnapshot(saved);
            TranslationExtractedStatus.Text=$"✓ 已将 {fresh.Length} 个新词批量加入「{pad.Title}」" + (wordsToAdd.Length>fresh.Length?$"（跳过 {wordsToAdd.Length-fresh.Length} 个已存在）":"");
            StatusText.Text=TranslationExtractedStatus.Text;
        }
        catch(Exception error)
        {
            TranslationExtractedStatus.Text="批量加入失败："+UserError(error);
        }
        finally
        {
            BatchAddExtractedButton.IsEnabled=_selectedExtracted.Count>0;
        }
    }
    private async void TranslateButton_Click(object sender,RoutedEventArgs e)=>await TranslateParagraphAsync();
    private async Task TranslateParagraphAsync()
    {
        var text=TranslationInput.Text;var pair=TranslationPair;
        if(string.IsNullOrWhiteSpace(text)){TranslationHintText.Text="请先输入需要翻译的段落。";return;}
        if(!_allowCloud){TranslationHintText.Text="离线模式下无法联网翻译。";return;}
        CancelTranslation();_translationCts=new CancellationTokenSource();var cancellation=_translationCts.Token;var version=_translationVersion;
        _translatedSource=text;_translatedText="";RenderTranslationText();
        TranslateButton.IsEnabled=false;TranslateButton.Content="翻译中…";TranslationHintText.Text="正在翻译…";
        RememberCurrentView();
        try
        {
            var translated=await _translation.TranslateAsync(text,pair,cancellation);
            if(version!=_translationVersion||_viewTarget!=ViewTarget.Translation)return;
            _translatedText=translated.Text;_translatedProvider=translated.Provider;RenderTranslationText();
            UiMotion.Reveal(TranslationOutputText);
            TranslationHintText.Text="点击英文单词查看释义。";
            TranslationScroll.ScrollToTop();RememberCurrentView();
        }
        catch(OperationCanceledException){ }
        catch(Exception error)
        {
            if(version==_translationVersion)TranslationHintText.Text=error is Services.TranslationException or ArgumentException?error.Message:"暂时无法翻译，请稍后重试。";
        }
        finally{if(version==_translationVersion){TranslateButton.IsEnabled=true;TranslateButton.Content="翻译";}}
    }
}
