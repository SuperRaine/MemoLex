using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.ComponentModel;
using System.Net;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private StubbornWordList? _stubbornWords;
    private DateTimeOffset? _stubbornFetched;
    private DateTimeOffset? _stubbornRetryAt;
    private CancellationTokenSource? _stubbornCts;
    private Task? _stubbornTask;
    private readonly StubbornWordRotation _stubbornRotation=new();
    private string? _stubbornError;
    private bool _stubbornLoading;
    private int _stubbornVersion,_stubbornLoaded,_stubbornTotal;
    private Window? _stubbornDialog;
    private event Action? StubbornChanged;
    internal Action<Window> PresentStubbornWords {get;set;}=dialog=>dialog.ShowDialog();

    private void ResetStubbornWords()
    {
        ++_stubbornVersion;_stubbornCts?.Cancel();_stubbornCts?.Dispose();_stubbornCts=null;_stubbornTask=null;
        _stubbornWords=null;_stubbornFetched=null;_stubbornRetryAt=null;_stubbornError=null;_stubbornLoading=false;_stubbornLoaded=_stubbornTotal=0;
        _stubbornRotation.Reset([],StubbornMode);
        UpdateStubbornCard();StubbornChanged?.Invoke();_stubbornDialog?.Close();
    }

    private void RestoreStubbornWords()
    {
        if (_allowCloud && _tokens.ActiveAccount is { } account)
            _stubbornWords = StubbornWordCache.Load(_tokens.ProfileDirectory, account.Id);
        ApplyStubbornOrder();
    }

    private string StubbornMeaning(string word)=>string.Join("；",(_dictionary.Lookup(word)?.translation??"").Replace("\\n","\n").Split('\n',StringSplitOptions.RemoveEmptyEntries));
    private StubbornSortMode StubbornMode=>_preferences.StubbornSort switch{"alphabetical"=>StubbornSortMode.Alphabetical,"random"=>StubbornSortMode.Random,_=>StubbornSortMode.Review};
    private void ApplyStubbornOrder(bool preserveCurrent=false)
    {
        _stubbornRotation.Reset(_stubbornWords?.Words??[],StubbornMode,_preferences.StubbornAlphabetDescending,preserveCurrent);
        UpdateStubbornCard();StubbornChanged?.Invoke();
    }
    private void UpdateStubbornCard()
    {
        if(StubbornWordText==null)return;
        var online=_allowCloud&&_tokens.HasToken;
        if(!online)StubbornWordText.Text="登录";
        else if(_stubbornRotation.Current is {} word)StubbornWordText.Text=word.Spelling;
        else
        {
            StubbornWordText.Text=_stubbornLoading?"刷新中…":_stubbornError!=null?"重试":"暂无";
        }
        var showCount=StubbornCard.Width>=210;
        StubbornCountText.Text=online&&_stubbornLoading&&_stubbornRotation.Current!=null
            ?"刷新中…":showCount&&_stubbornWords!=null?$"全部 {_stubbornWords.Total}":"全部";
        AllStubbornButton.ToolTip=_stubbornWords!=null?$"查看全部 {_stubbornWords.Total} 个顽固单词":"查看全部顽固单词";
        ShuffleStubbornButton.IsEnabled=online&&_stubbornRotation.Words.Count>1;
        ShuffleStubbornButton.ToolTip=StubbornMode==StubbornSortMode.Random?"下一个顽固单词（本轮不重复）":"下一个顽固单词";
        StubbornNextGlyph.Text=StubbornMode==StubbornSortMode.Random?"\uE8B1":"\uE72A";
        StubbornPreviewButton.ToolTip=_stubbornError??(!online?"登录后查看顽固单词":_stubbornRotation.Current==null?"查看顽固单词列表":$"查看 {_stubbornRotation.Current.Spelling} 的释义");
        AutomationProperties.SetName(StubbornPreviewButton,$"顽固单词：{StubbornWordText.Text}，点击查词");
    }
    private void HeaderCenterGrid_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        var available=Math.Max(1,e.NewSize.Width);StubbornCard.Width=Math.Min(270,available);
        StubbornLabelText.Visibility=available>=190?Visibility.Visible:Visibility.Collapsed;
        UpdateStubbornCard();
    }
    private async void StubbornWordButton_Click(object sender,RoutedEventArgs e)
    {
        if(!_allowCloud||!_tokens.HasToken){AccountButton_Click(sender,e);return;}
        if(_stubbornRotation.Current is {} word)await SearchAsync(word.Spelling);
        else StubbornPreviewButton_Click(sender,e);
    }
    internal Task RefreshStubbornWordsAsync(bool force=false)
    {
        if(!_ready||!_allowCloud||!_tokens.HasToken){UpdateStubbornCard();return Task.CompletedTask;}
        if(_stubbornTask is {IsCompleted:false})return _stubbornTask;
        if(!force&&_stubbornRetryAt is {} retry&&DateTimeOffset.UtcNow<retry)return Task.CompletedTask;
        if(!force&&_stubbornFetched!=null&&DateTimeOffset.UtcNow-_stubbornFetched<TimeSpan.FromMinutes(15))return Task.CompletedTask;
        _stubbornCts?.Cancel();_stubbornCts?.Dispose();_stubbornCts=CancellationTokenSource.CreateLinkedTokenSource(_accountCts.Token);
        var token=_stubbornCts.Token;var version=++_stubbornVersion;var account=_accountVersion;var key=_accountCredentialKey;
        var accountId=_tokens.ActiveAccount?.Id;
        _stubbornLoading=true;_stubbornError=null;_stubbornLoaded=_stubbornTotal=0;UpdateStubbornCard();StubbornChanged?.Invoke();
        bool Current()=>!token.IsCancellationRequested&&version==_stubbornVersion&&account==_accountVersion&&key==_tokens.CredentialKey;
        return _stubbornTask=LoadAsync();
        async Task LoadAsync()
        {
            try
            {
                var progress=new Progress<StubbornLoadProgress>(value=>{if(!Current()||!_stubbornLoading)return;_stubbornLoaded=value.Loaded;_stubbornTotal=value.Total;UpdateStubbornCard();StubbornChanged?.Invoke();});
                var words=await _api.GetStubbornWordsAsync(progress,token);
                if(!Current())return;
                _stubbornFetched=DateTimeOffset.UtcNow;_stubbornRetryAt=null;
                if(!words.Complete&&_stubbornWords is {Complete:true})
                {
                    _stubbornError=$"本次只收到 {words.Words.Count}/{words.Total} 个单词，仍显示上次的列表。";
                    return;
                }
                if(accountId!=null)StubbornWordCache.Save(_tokens.ProfileDirectory,accountId,words);
                _stubbornWords=words;
                ApplyStubbornOrder(true);
            }
            catch(OperationCanceledException){}
            catch(Exception error)
            {
                if(Current())
                {
                    _stubbornFetched=null;
                    _stubbornRetryAt=DateTimeOffset.UtcNow.AddSeconds(error is MaiMemoException {StatusCode:HttpStatusCode.TooManyRequests}?65:45);
                    _stubbornError=UserError(error);
                    SetAccountError(error);
                }
            }
            finally{if(Current()){_stubbornLoading=false;UpdateStubbornCard();StubbornChanged?.Invoke();}}
        }
    }
    private void ShuffleStubbornButton_Click(object sender,RoutedEventArgs e)
    {
        _stubbornRotation.Advance();UpdateStubbornCard();StubbornChanged?.Invoke();
    }
    private async void StubbornPreviewButton_Click(object sender,RoutedEventArgs e)
    {
        if(!_allowCloud||!_tokens.HasToken){AccountButton_Click(sender,e);return;}
        if(_stubbornDialog!=null){_stubbornDialog.Activate();return;}
        var account=_accountVersion;var credential=_accountCredentialKey;string? chosen=null;
        var dialog=InputDialog.Shell(this,"顽固单词",out _);_stubbornDialog=dialog;
        dialog.Width=730;dialog.Height=Math.Min(650,SystemParameters.WorkArea.Height-60);dialog.MinWidth=520;dialog.MinHeight=400;dialog.SizeToContent=SizeToContent.Manual;dialog.ResizeMode=ResizeMode.CanResize;
        System.Windows.Shell.WindowChrome.GetWindowChrome(dialog).ResizeBorderThickness=new Thickness(6);
        var body=new Grid{Margin=new Thickness(23,17,23,21)};
        for(var row=0;row<3;row++)body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        body.RowDefinitions.Add(new());body.RowDefinitions.Add(new(){Height=GridLength.Auto});InputDialog.SetBody(dialog,body);
        var header=new DockPanel{Margin=new Thickness(0,0,0,12)};body.Children.Add(header);
        var refresh=new Button{Content="刷新",VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(refresh,Dock.Right);header.Children.Add(refresh);
        var heading=new StackPanel();heading.Children.Add(new TextBlock{Text="顽固单词",FontSize=24,FontWeight=FontWeights.SemiBold,Foreground=Brush("#25516E")});var count=InputDialog.Text("");heading.Children.Add(count);header.Children.Add(heading);
        var filterRow=new Grid{Margin=new Thickness(0,0,0,12)};filterRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});filterRow.ColumnDefinitions.Add(new());Grid.SetRow(filterRow,1);body.Children.Add(filterRow);filterRow.Children.Add(new TextBlock{Text="筛选",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0),Foreground=Brush("#718DA3")});
        var filter=new TextBox{ToolTip="输入单词或中文释义"};Grid.SetColumn(filter,1);filterRow.Children.Add(filter);AutomationProperties.SetName(filter,"筛选顽固单词或释义");
        var options=new Grid{Margin=new Thickness(0,0,0,12)};
        for(var column=0;column<3;column++)options.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        options.ColumnDefinitions.Add(new());Grid.SetRow(options,2);body.Children.Add(options);
        options.Children.Add(new TextBlock{Text="排序",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0),Foreground=Brush("#718DA3")});
        var sort=new ComboBox{Width=136,Height=34,ItemsSource=new[]{new StubbornSortChoice("review","复习时间"),new StubbornSortChoice("alphabetical","字母顺序"),new StubbornSortChoice("random","随机轮换")},DisplayMemberPath=nameof(StubbornSortChoice.Label),SelectedValuePath=nameof(StubbornSortChoice.Key),SelectedValue=_preferences.StubbornSort};
        Grid.SetColumn(sort,1);options.Children.Add(sort);AutomationProperties.SetName(sort,"顽固单词排序");
        var direction=new Button{Width=66,Height=34,Padding=new Thickness(7,4,7,4),Margin=new Thickness(7,0,0,0),Content=_preferences.StubbornAlphabetDescending?"Z–A":"A–Z",Visibility=StubbornMode==StubbornSortMode.Alphabetical?Visibility.Visible:Visibility.Collapsed,ToolTip="切换字母顺序"};
        Grid.SetColumn(direction,2);options.Children.Add(direction);AutomationProperties.SetName(direction,"切换字母顺序");
        var conceal=new CheckBox{Content="遮住释义",Style=(Style)FindResource("SwitchCheckBox"),IsChecked=_preferences.HideStubbornMeanings,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(12,0,0,0)};
        Grid.SetColumn(conceal,3);options.Children.Add(conceal);
        var list=new ListView{Background=Brushes.White,BorderBrush=Brush("#DCE9F3"),BorderThickness=new Thickness(1)};Grid.SetRow(list,3);body.Children.Add(list);VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        var columns=new GridView();list.View=columns;
        var wordButton=new FrameworkElementFactory(typeof(Button));wordButton.SetValue(StyleProperty,FindResource("StubbornWordButton"));wordButton.SetBinding(ContentControl.ContentProperty,new Binding(nameof(StubbornEntry.Word)));wordButton.SetBinding(FrameworkElement.TagProperty,new Binding(nameof(StubbornEntry.Word)));wordButton.SetValue(Control.BackgroundProperty,Brushes.Transparent);wordButton.SetValue(Control.BorderThicknessProperty,new Thickness(0));wordButton.SetValue(Control.ForegroundProperty,Brush("#087FAF"));wordButton.SetValue(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Left);wordButton.SetValue(Control.PaddingProperty,new Thickness(4,7,8,7));wordButton.AddHandler(Button.ClickEvent,new RoutedEventHandler((button,args)=>{if(button is Button{Tag:string word}){chosen=word;dialog.DialogResult=true;}}));
        columns.Columns.Add(new GridViewColumn{Header="单词",Width=190,CellTemplate=new DataTemplate{VisualTree=wordButton}});
        var meaningStyle=new Style(typeof(Button),(Style)FindResource("StubbornWordButton"));
        meaningStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));meaningStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));
        var concealedStyle=new DataTrigger{Binding=new Binding(nameof(StubbornEntry.IsConcealed)),Value=true};concealedStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#EEF5FA")));concealedStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#809BB0")));meaningStyle.Triggers.Add(concealedStyle);
        var meaningButton=new FrameworkElementFactory(typeof(Button));meaningButton.SetValue(StyleProperty,meaningStyle);meaningButton.SetBinding(TagProperty,new Binding());meaningButton.SetValue(Control.PaddingProperty,new Thickness(7,7,7,7));meaningButton.SetBinding(ToolTipProperty,new Binding(nameof(StubbornEntry.DisplayMeaning)));
        var meaningText=new FrameworkElementFactory(typeof(TextBlock));meaningText.SetBinding(TextBlock.TextProperty,new Binding(nameof(StubbornEntry.DisplayMeaning)));meaningText.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);meaningButton.AppendChild(meaningText);
        meaningButton.AddHandler(Button.ClickEvent,new RoutedEventHandler((button,args)=>{if(button is Button{Tag:StubbornEntry entry}){entry.ToggleMeaning();args.Handled=true;}}));
        var meaningColumn=new GridViewColumn{Header="释义",Width=440,CellTemplate=new DataTemplate{VisualTree=meaningButton}};columns.Columns.Add(meaningColumn);list.SizeChanged+=(_,_)=>meaningColumn.Width=Math.Max(200,list.ActualWidth-218);
        var hint=InputDialog.Text("");hint.FontSize=11;hint.Margin=new Thickness(0,11,0,0);Grid.SetRow(hint,4);body.Children.Add(hint);
        var entries=new Dictionary<string,StubbornEntry>(StringComparer.OrdinalIgnoreCase);StubbornWordList? displayed=null;
        IReadOnlyList<StubbornWord>? renderedOrder=null;
        string? renderedFilter=null;
        bool definitionsReady=false;
        var visibleCount=0;
        void Render()
        {
            var filterText=filter.Text.Trim();
            if(!ReferenceEquals(renderedOrder,_stubbornRotation.Words)||filterText!=renderedFilter||definitionsReady!=_ready)
            {
                if(!ReferenceEquals(displayed,_stubbornWords)||definitionsReady!=_ready)entries.Clear();
                displayed=_stubbornWords;renderedOrder=_stubbornRotation.Words;renderedFilter=filterText;definitionsReady=_ready;
                var visible=renderedOrder.Select(word=>
                {
                    if(!entries.TryGetValue(word.Spelling,out var entry))entries[word.Spelling]=entry=new(word.Spelling,StubbornMeaning(word.Spelling),_preferences.HideStubbornMeanings);
                    return entry;
                }).Where(item=>item.Word.Contains(filterText,StringComparison.OrdinalIgnoreCase)||item.Meaning.Contains(filterText,StringComparison.OrdinalIgnoreCase)).ToArray();
                list.ItemsSource=visible;
                visibleCount=visible.Length;
            }
            var progress=_stubbornTotal>0?$" · {_stubbornLoaded}/{_stubbornTotal}":"";
            count.Text=_stubbornLoading?$"刷新中…{progress}":$"{_tokens.ActiveAccount?.Name??"当前账号"} · {visibleCount} 个单词";
            refresh.Content=_stubbornLoading?"刷新中…":"刷新";
            refresh.IsEnabled=!_stubbornLoading;
            hint.Text=_stubbornError!=null?"刷新未完成："+_stubbornError:displayed is {Complete:false}?$"已获取 {displayed.Words.Count}/{displayed.Total}，部分记录受接口单次上限影响，可稍后刷新。":entries.Count==0&&!_stubbornLoading?"还没有已同步的顽固单词，在墨墨 App 同步后刷新。":_preferences.HideStubbornMeanings?"点击释义区域揭示答案，点击单词查词。":"点击单词查词；手机端同步学习数据后刷新。";
        }
        sort.SelectionChanged+=(_,_)=>
        {
            if(sort.SelectedValue is not string mode)return;
            _preferences.StubbornSort=mode;_preferences.Save();direction.Visibility=mode=="alphabetical"?Visibility.Visible:Visibility.Collapsed;ApplyStubbornOrder();
        };
        direction.Click+=(_,_)=>{_preferences.StubbornAlphabetDescending=!_preferences.StubbornAlphabetDescending;_preferences.Save();direction.Content=_preferences.StubbornAlphabetDescending?"Z–A":"A–Z";ApplyStubbornOrder();};
        void ToggleConceal(object sender,RoutedEventArgs args)
        {
            _preferences.HideStubbornMeanings=conceal.IsChecked==true;_preferences.Save();
            foreach(var entry in entries.Values)entry.SetConceal(_preferences.HideStubbornMeanings);Render();
        }
        conceal.Checked+=ToggleConceal;conceal.Unchecked+=ToggleConceal;
        refresh.Click+=async(_,_)=>await RefreshStubbornWordsAsync(true);filter.TextChanged+=(_,_)=>Render();StubbornChanged+=Render;
        dialog.RegisterName("StubbornWordsList",list);dialog.RegisterName("StubbornFilter",filter);dialog.RegisterName("RefreshStubbornWords",refresh);dialog.RegisterName("StubbornListCount",count);
        dialog.RegisterName("StubbornSort",sort);dialog.RegisterName("StubbornSortDirection",direction);dialog.RegisterName("HideStubbornMeanings",conceal);dialog.RegisterName("StubbornOptions",options);
        var editing=_editingSettings;_editingSettings=true;ReleaseGlobalShortcut();if(_hover!=null)_hover.Enabled=false;
        try{_ = RefreshStubbornWordsAsync(_stubbornError!=null);Render();PresentStubbornWords(dialog);}
        finally{StubbornChanged-=Render;_stubbornDialog=null;_editingSettings=editing;if(!_exitRequested&&IsVisible&&!editing){RefreshGlobalShortcut();RefreshDoubleCopy();RefreshHoverLookup();}}
        if(chosen!=null&&account==_accountVersion&&credential==_tokens.CredentialKey)await SearchAsync(chosen);
    }
    private sealed record StubbornSortChoice(string Key,string Label);
    private sealed class StubbornEntry(string word,string meaning,bool conceal):INotifyPropertyChanged
    {
        private bool _conceal=conceal,_revealed;
        public string Word {get;}=word;
        public string Meaning {get;}=meaning;
        public bool IsConcealed=>_conceal&&!_revealed;
        public string DisplayMeaning=>IsConcealed?"点击查看释义":Meaning.Length==0?"暂无释义":Meaning;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void SetConceal(bool conceal){_conceal=conceal;_revealed=false;Changed();}
        internal void ToggleMeaning(){if(!_conceal)return;_revealed=!_revealed;Changed();}
        private void Changed(){PropertyChanged?.Invoke(this,new(nameof(IsConcealed)));PropertyChanged?.Invoke(this,new(nameof(DisplayMeaning)));}
    }
}
