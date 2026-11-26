using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.IO;
using MemoLex.Core;
using MemoLex.Desktop;

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
        StopPronunciation();
        _lookupCts?.Cancel();_examplesCts?.Cancel();++_wordVersion;++_membershipVersion;
        _viewTarget=ViewTarget.ChineseSearch;ResultScroll.Visibility=TranslationView.Visibility=Visibility.Collapsed;ChineseSearchView.Visibility=Visibility.Visible;
    }
    private async Task SearchChineseAsync(string raw,bool recordHistory=true,bool addNavigationEntry=true,ChineseSnapshot? restore=null)
    {
        if(!_ready)return;
        var query=raw.Trim();
        if(ChineseReverseSearch.Normalize(query).Length is <1 or >40||query.Length>100){StatusText.Text="请输入 1 到 40 个汉字。";return;}
        if(addNavigationEntry)RememberCurrentView();
        CancelChineseSearch();CancelTranslation();_chineseCts=new CancellationTokenSource();var cancellation=_chineseCts.Token;var version=_chineseVersion;
        _navigating=true;EnterChineseView();_chineseQuery=query;SearchBox.Text=query;_chineseItems=[];_chineseTotal=0;_chineseReady=false;
        ChineseQueryTitle.Text="「"+query+"」的英文表达";ChineseSearchHint.Text="正在查找相关词…";ChineseResultsPanel.Children.Clear();
        ChineseSearchScroll.ScrollToTop();MoreChineseResultsButton.Visibility=Visibility.Collapsed;UpdateHeader();_navigating=false;
        if(addNavigationEntry)_navigation.Navigate(CaptureView());UpdateNavigationButtons();
        if(recordHistory){_preferences.RecordLookup(query);_preferences.Save();UpdateHistory();}
        try
        {
            var page=restore?.Ready==true?new ChineseSearchPage(query,restore.Items,restore.Total):await _chinese.SearchAsync(query,cancellationToken:cancellation);
            if(version!=_chineseVersion||cancellation.IsCancellationRequested)return;
            ApplyChinesePage(page);_chineseReady=true;
            UiMotion.Reveal(ChineseSearchScroll);
            if(restore!=null)QueueViewRestore(_wordVersion,()=>{ChineseSearchScroll.UpdateLayout();ChineseSearchScroll.ScrollToVerticalOffset(restore.ScrollOffset);});
            RememberCurrentView();
        }
        catch(OperationCanceledException){ }
        catch(Exception error)
        {
            if(version==_chineseVersion){_chineseReady=true;ChineseSearchHint.Text=error is IOException or ArgumentException?error.Message:"查词失败，请重试。";RememberCurrentView();}
        }
    }
    private void ApplyChinesePage(ChineseSearchPage page)
    {
        _chineseItems=page.Items.ToArray();_chineseTotal=page.Total;ChineseResultsPanel.Children.Clear();
        ChineseSearchHint.Text=page.Total==0
            ?"未找到相关词。请缩短查询内容。"
            :$"共 {page.Total} 个相关词。点击一行查看释义。";
        MoreChineseResultsButton.Visibility=page.Total>page.Items.Count&&page.Items.Count<120?Visibility.Visible:Visibility.Collapsed;
        MoreChineseResultsButton.IsEnabled=true;
        foreach(var item in page.Items)
        {
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(154)});body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var heading=new TextBlock {Text=item.Word,FontSize=15.5,FontFamily=new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI"),Foreground=Brush("#0A7FB3"),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,12,0),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,ToolTip=item.Word};
            body.Children.Add(heading);
            var matchedMeaning = item.DisplayMatchedMeaning;
            var meaning=new TextBlock {FontSize=13.5,LineHeight=22,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=Brush("#33536B"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0),ToolTip=item.DisplayMeaning};Grid.SetColumn(meaning,1);
            foreach(var part in TextHighlights.Split(matchedMeaning,new[]{ChineseReverseSearch.Normalize(_chineseQuery)}))
                meaning.Inlines.Add(new Run(part.Text){Background=part.Highlighted?Brush("#D2EFFE"):System.Windows.Media.Brushes.Transparent,Foreground=part.Highlighted?Brush("#085680"):Brush("#33536B"),FontWeight=part.Highlighted?FontWeights.SemiBold:FontWeights.Normal});
            body.Children.Add(meaning);
            var kindBadge=new Border
            {
                Background=Brush(item.IsExactMatch?"#E4F5FF":"#F1F7FB"),
                BorderBrush=Brush(item.IsExactMatch?"#C0E4FA":"#E0ECF5"),
                BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),
                Padding=new Thickness(8,2,8,2),VerticalAlignment=VerticalAlignment.Center,
                Child=new TextBlock{Text=item.Kind,FontSize=10.5,FontWeight=FontWeights.SemiBold,Foreground=Brush(item.IsExactMatch?"#0A7FB3":"#7492A8")}
            };
            Grid.SetColumn(kindBadge,2);body.Children.Add(kindBadge);
            var row=new Button
            {
                Content=body, HorizontalContentAlignment=HorizontalAlignment.Stretch,
                Background=System.Windows.Media.Brushes.White,
                BorderBrush=Brush("#DCEBF7"), Padding=new Thickness(15,10,14,10),
                Margin=new Thickness(0,0,0,6), ContextMenu=HaiciWordMenu(item.Word)
            };
            AutomationProperties.SetName(row,$"{item.Word}，{matchedMeaning}");
            row.Click+=async(_,_)=>await LoadWordAsync(item.Word);
            ChineseResultsPanel.Children.Add(row);
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
        catch {if(version==_chineseVersion){ChineseSearchHint.Text="未能加载更多结果，请重试。";MoreChineseResultsButton.IsEnabled=true;}}
    }
    private async Task UpdateSuggestionsAsync(string query)
    {
        _suggestionCts?.Cancel();_suggestionCts?.Dispose();_suggestionCts=new CancellationTokenSource();var cancellation=_suggestionCts.Token;
        if(!ChineseReverseSearch.ContainsChinese(query))
        {
            var normalized=ExampleText.NormalizeLookup(query);
            var list=_dictionary.Suggest(normalized,25);
            if(list.Count==0&&normalized.Length>=3)list=_dictionary.SuggestSpellingCorrections(normalized,10);
            _changingLists=true;SuggestionList.ItemsSource=list;_changingLists=false;return;
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
