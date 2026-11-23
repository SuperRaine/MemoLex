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
    private void RenderTranslationText()
    {
        FillWordLinks(TranslationSourceText,_translatedSource.Length>0?_translatedSource:"在上方输入或粘贴原文。",EmptyMatches,_word,navigate:NavigateTranslationWordAsync);
        FillWordLinks(TranslationOutputText,_translatedText,EmptyMatches,_word,navigate:NavigateTranslationWordAsync);
        TranslationOutputText.ToolTip = _translatedText.Length > 0 && _translatedProvider.Length > 0 ? "翻译来源 " + _translatedProvider : null;
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
