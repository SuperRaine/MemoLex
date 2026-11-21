using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.IO;
using MemoLex.Core;

namespace MemoLex;

public partial class MainWindow
{
    private sealed record ChineseSnapshot(string Query,ChineseSearchResult[] Items,int Total,bool Ready,double ScrollOffset);
    private readonly ChineseReverseSearch _chinese;
    private CancellationTokenSource? _chineseCts,_suggestionCts;
    private int _chineseVersion;
    private string _chineseQuery="";
    private ChineseSearchResult[] _chineseItems=[];
    private int _chineseTotal;
    private bool _chineseReady;
    private ChineseSnapshot CaptureChinese()=>new(_chineseQuery,_chineseItems,_chineseTotal,_chineseReady,ChineseSearchScroll.VerticalOffset);
    private Task SearchAsync(string input,bool recordHistory=true) => ChineseReverseSearch.ContainsChinese(input)?SearchChineseAsync(input,recordHistory):LoadWordAsync(input,recordHistory);
    private void CancelChineseSearch(){++_chineseVersion;_chineseCts?.Cancel();_chineseCts?.Dispose();_chineseCts=null;}
    private void ChineseSearchScroll_ScrollChanged(object sender,ScrollChangedEventArgs e)
    {
        if(e.VerticalChange!=0&&ReferenceEquals(e.Source,ChineseSearchScroll)&&_viewTarget==ViewTarget.ChineseSearch)RememberCurrentView();
    }
    private void EnterChineseView()
    {
        _lookupCts?.Cancel();_examplesCts?.Cancel();++_wordVersion;++_membershipVersion;
        _viewTarget=ViewTarget.ChineseSearch;ResultScroll.Visibility=TranslationView.Visibility=Visibility.Collapsed;ChineseSearchView.Visibility=Visibility.Visible;
    }
    private async Task SearchChineseAsync(string raw,bool recordHistory=true,bool addNavigationEntry=true,ChineseSnapshot? restore=null)
    {
        if(!_ready)return;
        var query=raw.Trim();
        if(ChineseReverseSearch.Normalize(query).Length is <1 or >40||query.Length>100){StatusText.Text="请输入 1–40 个汉字的中文含义。";return;}
        if(addNavigationEntry)RememberCurrentView();
        CancelChineseSearch();CancelTranslation();_chineseCts=new CancellationTokenSource();var cancellation=_chineseCts.Token;var version=_chineseVersion;
        _navigating=true;EnterChineseView();_chineseQuery=query;SearchBox.Text=query;_chineseItems=[];_chineseTotal=0;_chineseReady=false;
        ChineseQueryTitle.Text="中文反查 · "+query;ChineseSearchHint.Text="正在查找相关英文词与同义词…";ChineseResultsPanel.Children.Clear();
        ChineseSearchScroll.ScrollToTop();MoreChineseResultsButton.Visibility=Visibility.Collapsed;UpdateHeader();_navigating=false;
        if(addNavigationEntry)_navigation.Navigate(CaptureView());UpdateNavigationButtons();
        if(recordHistory){_preferences.RecordLookup(query);_preferences.Save();UpdateHistory();}
        try
        {
            var page=restore?.Ready==true?new ChineseSearchPage(query,restore.Items,restore.Total):await _chinese.SearchAsync(query,cancellationToken:cancellation);
            if(version!=_chineseVersion||cancellation.IsCancellationRequested)return;
            ApplyChinesePage(page);_chineseReady=true;
            if(restore!=null)QueueViewRestore(_wordVersion,()=>{ChineseSearchScroll.UpdateLayout();ChineseSearchScroll.ScrollToVerticalOffset(restore.ScrollOffset);});
            RememberCurrentView();
        }
        catch(OperationCanceledException){ }
        catch(Exception error)
        {
            if(version==_chineseVersion){_chineseReady=true;ChineseSearchHint.Text=error is IOException or ArgumentException?error.Message:"中文反查未完成，请重试。";RememberCurrentView();}
        }
    }
    private void ApplyChinesePage(ChineseSearchPage page)
    {
        _chineseItems=page.Items.ToArray();_chineseTotal=page.Total;ChineseResultsPanel.Children.Clear();
        ChineseSearchHint.Text=page.Total==0?"没有找到贴近该含义的词。可尝试更简洁的中文词义，如“快乐”“管理”“健谈”。":$"已显示 {page.Items.Count} 条 · 共 {page.Total} 条相关结果 · 点击英文词查看完整释义";
        MoreChineseResultsButton.Visibility=page.Total>page.Items.Count&&page.Items.Count<120?Visibility.Visible:Visibility.Collapsed;
        MoreChineseResultsButton.IsEnabled=true;
        foreach(var item in page.Items)
        {
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(145)});body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var heading=new TextBlock {FontSize=16,FontFamily=new System.Windows.Media.FontFamily("Segoe UI"),Margin=new Thickness(0,0,12,0),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,ToolTip=item.Word};
            var link=new Hyperlink(new Run(item.Word)){Foreground=Brush("#087FAF"),TextDecorations=null,FontWeight=FontWeights.SemiBold,ToolTip="查询 "+item.Word};
            link.ContextMenu=HaiciWordMenu(item.Word);
            link.Click+=async(_,_)=>await LoadWordAsync(item.Word);heading.Inlines.Add(link);body.Children.Add(heading);
            var meaning=new TextBlock {FontSize=13,LineHeight=22,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=Brush("#4F6D85"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0),ToolTip=item.Meaning};Grid.SetColumn(meaning,1);
            foreach(var part in TextHighlights.Split(item.MatchedMeaning,new[]{ChineseReverseSearch.Normalize(_chineseQuery)}))
                meaning.Inlines.Add(new Run(part.Text){Background=part.Highlighted?Brush("#E1F4FF"):System.Windows.Media.Brushes.Transparent,FontWeight=part.Highlighted?FontWeights.SemiBold:FontWeights.Normal});
            body.Children.Add(meaning);var kind=new TextBlock {Text=item.Kind,FontSize=10,Foreground=Brush(item.IsExactMatch?"#1688B7":"#99ACBA"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(kind,2);body.Children.Add(kind);
            ChineseResultsPanel.Children.Add(new Border {Child=body,Background=System.Windows.Media.Brushes.White,BorderBrush=Brush("#E2EBF3"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(13,8,13,8),Margin=new Thickness(0,0,0,4)});
        }
    }
    private async void MoreChineseResultsButton_Click(object sender,RoutedEventArgs e)
    {
        if(!_chineseReady||_viewTarget!=ViewTarget.ChineseSearch)return;
        var version=_chineseVersion;var offset=ChineseSearchScroll.VerticalOffset;MoreChineseResultsButton.IsEnabled=false;
        try
        {
            var page=await _chinese.SearchAsync(_chineseQuery,Math.Min(120,_chineseItems.Length+40),_chineseCts?.Token??CancellationToken.None);
            if(version!=_chineseVersion)return;
            ApplyChinesePage(page);QueueViewRestore(_wordVersion,()=>{ChineseSearchScroll.UpdateLayout();ChineseSearchScroll.ScrollToVerticalOffset(offset);});RememberCurrentView();
        }
        catch(OperationCanceledException){ }
        catch {if(version==_chineseVersion){ChineseSearchHint.Text="更多结果读取未完成，请重试。";MoreChineseResultsButton.IsEnabled=true;}}
    }
    private async Task UpdateSuggestionsAsync(string query)
    {
        _suggestionCts?.Cancel();_suggestionCts?.Dispose();_suggestionCts=new CancellationTokenSource();var cancellation=_suggestionCts.Token;
        if(!ChineseReverseSearch.ContainsChinese(query))
        {
            _changingLists=true;SuggestionList.ItemsSource=_dictionary.Suggest(ExampleText.NormalizeLookup(query),25);_changingLists=false;return;
        }
        _changingLists=true;SuggestionList.ItemsSource=null;_changingLists=false;
        try
        {
            await Task.Delay(220,cancellation);
            var page=await _chinese.SearchAsync(query,10,cancellation);
            if(cancellation.IsCancellationRequested||SearchBox.Text!=query)return;
            _changingLists=true;SuggestionList.ItemsSource=page.Items;_changingLists=false;
        }
        catch(OperationCanceledException){ }
        catch { }
    }
}
