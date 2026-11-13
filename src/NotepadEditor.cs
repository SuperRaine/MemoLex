using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;

internal static class NotepadEditor
{
    internal static Window Build(Window owner,CloudNotepad original,MaiMemoClient api,Action<CloudNotepad> saved,LocalDictionary? dictionary=null,Func<string,Task>? lookup=null,Action<string>? deleted=null,Func<Window,CloudNotepad,int,bool>? confirmDeletion=null)
    {
        var dialog=InputDialog.Shell(owner,"我的云词本",out _);
        dialog.Width=800;dialog.Height=730;dialog.MinWidth=620;dialog.MinHeight=520;
        dialog.SizeToContent=SizeToContent.Manual;dialog.ResizeMode=ResizeMode.CanResize;
        WindowChrome.GetWindowChrome(dialog).ResizeBorderThickness=new Thickness(6);
        var root=new Grid {Margin=new Thickness(20,14,20,18)};
        foreach(var height in new[]{new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})root.RowDefinitions.Add(new RowDefinition {Height=height});
        var tabs=new TabControl();root.Children.Add(tabs);
        var browse=new Grid {Margin=new Thickness(10,10,10,4)};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})browse.RowDefinitions.Add(new RowDefinition {Height=height});
        void Add(UIElement item,int row){Grid.SetRow(item,row);browse.Children.Add(item);}
        var titleDisplay=new TextBlock {Text=original.Title,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10),TextTrimming=TextTrimming.CharacterEllipsis};Add(titleDisplay,0);
        var fields=new StackPanel {Margin=new Thickness(18,15,18,15)};
        fields.Children.Add(new TextBlock {Text="云词本信息",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,18)});
        fields.Children.Add(InputDialog.Text("词本名称"));
        var title=new TextBox {Text=original.Title,MaxLength=100,Margin=new Thickness(0,4,0,8)};fields.Children.Add(title);
        fields.Children.Add(InputDialog.Text("简介"));var brief=new TextBox {Text=original.Brief,MaxLength=2000,Margin=new Thickness(0,4,0,18),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=110,VerticalContentAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};fields.Children.Add(brief);
        var tagsInfo=InputDialog.Text("标签："+(original.Tags.Count==0?"无":string.Join("、",original.Tags)));fields.Children.Add(tagsInfo);
        string StatusLabel(string value)=>value switch {"UNPUBLISHED"=>"未发布","PUBLISHED"=>"已发布","DELETED"=>"已删除",_=>value};
        var publicationInfo=InputDialog.Text("发布状态："+StatusLabel(original.Status));fields.Children.Add(publicationInfo);
        fields.Children.Add(new TextBlock {Text="保存时保留现有标签和发布状态。",FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(140,161,179)),Margin=new Thickness(0,12,0,0),TextWrapping=TextWrapping.Wrap});
        var metadata=new ScrollViewer {Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var searchPanel=new DockPanel {Margin=new Thickness(0,0,0,9)};
        searchPanel.Children.Add(new TextBlock {Text="筛选",Width=42,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(88,113,138))});
        var search=new TextBox {ToolTip="按英文或中文释义筛选"};searchPanel.Children.Add(search);Add(searchPanel,1);
        var toolbar=new WrapPanel {Margin=new Thickness(0,0,0,7)};
        Button Tool(string label){var button=new Button {Content=label,FontSize=11,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,0,7,4)};toolbar.Children.Add(button);return button;}
        var selectAll=Tool("全选当前结果");var clear=Tool("清除选择");var delete=Tool("删除选中");Add(toolbar,2);
        var entryPanel=new Expander {Header="添加 / 编辑词条与分组",Margin=new Thickness(0,0,0,8)};
        var entryBody=new Grid {Margin=new Thickness(0,8,0,5)};foreach(var height in new[]{GridLength.Auto,GridLength.Auto})entryBody.RowDefinitions.Add(new RowDefinition {Height=height});
        var entryInput=new TextBox {Text=original.Content,AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,Height=150,VerticalContentAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=300000,FontFamily=new FontFamily("Consolas, Microsoft YaHei UI")};entryBody.Children.Add(entryInput);
        var entryFooter=new DockPanel {Margin=new Thickness(0,6,0,0)};Grid.SetRow(entryFooter,1);entryBody.Children.Add(entryFooter);
        var entryAction=new Button {Content="完成编辑",FontSize=11,Padding=new Thickness(12,5,12,5),HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(entryAction,Dock.Right);entryFooter.Children.Add(entryAction);
        var entryHint=InputDialog.Text("直接增删词条、调整顺序；# 开头为分组。支持粘贴多词并自动去重，列表实时更新。");entryFooter.Children.Add(entryHint);entryPanel.Content=entryBody;Add(entryPanel,3);
        var list=new ListBox {HorizontalContentAlignment=HorizontalAlignment.Stretch,SelectionMode=SelectionMode.Extended,ToolTip="单击选择；Ctrl 点选多个，Shift 连续选择；Delete 删除；双击查词"};Add(list,4);
        VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        var itemStyle=new Style(typeof(ListBoxItem),(Style)Application.Current.FindResource(typeof(ListBoxItem)));
        itemStyle.Setters.Add(new Setter(Control.FontFamilyProperty,new FontFamily("Microsoft YaHei UI")));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,4,5,4)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty,Cursors.Arrow));
        var chapterItem=new DataTrigger {Binding=new Binding("IsChapter"),Value=true};chapterItem.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty,false));chapterItem.Setters.Add(new Setter(UIElement.FocusableProperty,false));itemStyle.Triggers.Add(chapterItem);list.ItemContainerStyle=itemStyle;
        list.ItemTemplate=WordTemplate();
        var count=InputDialog.Text("正在准备中文释义…");count.Margin=new Thickness(0,7,0,0);count.FontSize=11;Add(count,5);
        // Keep the list and save controls reachable with the editor expanded.
        var upper=new Grid();for(var row=0;row<4;row++)upper.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        foreach(var item in browse.Children.Cast<UIElement>().Where(item=>Grid.GetRow(item)<4).ToArray()){browse.Children.Remove(item);upper.Children.Add(item);}
        browse.RowDefinitions.Clear();browse.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});browse.RowDefinitions.Add(new RowDefinition());browse.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var upperScroll=new ScrollViewer {Content=upper,MaxHeight=360,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};browse.Children.Add(upperScroll);Grid.SetRow(list,1);Grid.SetRow(count,2);
        browse.SizeChanged+=(_,e)=>upperScroll.MaxHeight=Math.Max(100,e.NewSize.Height-150);
        tabs.Items.Add(new TabItem {Header="单词与释义",Content=browse});
        tabs.Items.Add(new TabItem {Header="编辑词本",Content=metadata});
        var status=InputDialog.Text("单击选择 · Ctrl 点选多个 · Shift 连选 · Delete 删除 · 双击查词。修改后点击保存同步云端。");status.Margin=new Thickness(0,9,0,10);Grid.SetRow(status,1);root.Children.Add(status);
        tabs.SelectionChanged+=(_,e)=>
        {
            if(ReferenceEquals(e.Source,tabs))status.Text=tabs.SelectedIndex==0?"单击选择 · Ctrl 点选多个 · Shift 连选 · Delete 删除 · 双击查词。":"编辑云词本名称与简介；保存时保留现有标签和发布状态。";
        };
        var buttons=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(buttons,2);root.Children.Add(buttons);
        Button Footer(string label){var button=new Button {Content=label,Margin=new Thickness(7,0,0,0),FontSize=12};buttons.Children.Add(button);return button;}
        var undo=Footer("撤销修改");var reload=Footer("重新载入");reload.ToolTip="载入云端内容，替换尚未保存的修改";
        var close=Footer("关闭");var save=Footer("保存到云词本");save.Style=(Style)Application.Current.FindResource("AccentButton");
        InputDialog.SetBody(dialog,root);
        foreach(var control in new (string,FrameworkElement)[]{("NotepadWords",list),("NotepadFilter",search),("NotepadTitle",title),("NotepadBrief",brief),("NotepadEntryInput",entryInput),("NotepadEntryAction",entryAction),("NotepadEntryPanel",entryPanel),("NotepadDeleteSelected",delete),("NotepadSelectAll",selectAll),("NotepadClearSelection",clear),("NotepadSave",save),("NotepadUndo",undo),("NotepadTabs",tabs),("NotepadMetadata",metadata),("NotepadStatus",status)})dialog.RegisterName(control.Item1,control.Item2);
        var rows=Array.Empty<NotepadDisplayRow>();
        var totalWords=0;
        var draftContent=original.Content;
        var lastNonEmptyContent=original.Content;
        var changing=false;var busy=false;var closed=false;var version=0;var lifetime=new CancellationTokenSource();CancellationTokenSource? rowsCts=null;
        bool Dirty()=>title.Text!=original.Title||brief.Text!=original.Brief||draftContent!=original.Content;
        string[] SelectedWords()=>list.SelectedItems.OfType<NotepadDisplayRow>().Where(row=>!row.IsChapter).Select(row=>row.Word).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        void UpdateState()
        {
            var query=search.Text.Trim();var shown=rows.Count(row=>!row.IsChapter&&(query.Length==0||row.Word.Contains(query,StringComparison.OrdinalIgnoreCase)||row.Meaning.Contains(query,StringComparison.OrdinalIgnoreCase)));
            var selectedCount=SelectedWords().Length;
            count.Text=$"{shown} / {rows.Count(row=>!row.IsChapter)} 个单词 · 已选 {selectedCount} 个 · Ctrl / Shift 多选";
            delete.IsEnabled=!busy&&selectedCount>0;clear.IsEnabled=!busy&&list.SelectedItems.Count>0;save.IsEnabled=undo.IsEnabled=!busy&&Dirty();
            delete.Content=selectedCount>0&&selectedCount==totalWords?"删除云词本…":"删除选中";
            entryAction.IsEnabled=!busy;
        }
        void Filter()
        {
            var query=search.Text.Trim();changing=true;
            try
            {
                list.ItemsSource=rows.Where(row=>query.Length==0||!row.IsChapter&&(row.Word.Contains(query,StringComparison.OrdinalIgnoreCase)||row.Meaning.Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray();
            }
            finally{changing=false;}UpdateState();
        }
        async Task RefreshRows(bool debounce=false)
        {
            rowsCts?.Cancel();rowsCts?.Dispose();rowsCts=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var cancellation=rowsCts.Token;var current=++version;var snapshot=draftContent;
            try
            {
                if(debounce)await Task.Delay(180,cancellation);
                var result=await Task.Run(()=>NotepadContent.Describe(snapshot,word=>dictionary?.Lookup(word)?.translation,cancellation),cancellation);
                if(closed||current!=version||cancellation.IsCancellationRequested)return;rows=result.ToArray();totalWords=rows.Where(row=>!row.IsChapter).Select(row=>row.Word).Distinct(StringComparer.OrdinalIgnoreCase).Count();if(totalWords>0)lastNonEmptyContent=snapshot;Filter();
            }
            catch(OperationCanceledException){ }
            catch{if(!closed)status.Text="中文释义载入失败，请重新载入后重试。";}
        }
        async Task SetContent(string value)
        {
            draftContent=value;changing=true;try{entryInput.Text=value;}finally{changing=false;}await RefreshRows();UpdateState();
        }
        async Task Remove(IEnumerable<string> words)
        {
            if(busy)return;
            var removed=words.ToArray();if(removed.Length==0)return;
            var remaining=NotepadContent.RemoveWords(draftContent,removed);
            if(NotepadContent.Words(remaining).Count==0){await DeleteBook();return;}
            await SetContent(remaining);status.Text=$"已从列表移除 {removed.Distinct(StringComparer.OrdinalIgnoreCase).Count()} 个单词 · 保存后同步云端。";
        }
        async Task DeleteBook()
        {
            if(busy)return;
            var count=NotepadContent.Words(draftContent).Count;
            if(!(confirmDeletion??NotepadDeletionDialog.Confirm)(dialog,original,count==0?NotepadContent.Words(original.Content).Count:count))
            {status.Text="已取消删除云词本，云端内容已保留。";return;}
            Busy(true);status.Text="正在删除整个云词本…";
            try{await api.DeleteNotepadAsync(original);deleted?.Invoke(original.Id);Busy(false);dialog.Close();}
            catch(Exception error){status.Text=error is MaiMemoException?error.Message:"删除结果尚未确认，请重新载入检查。";}
            finally{if(!closed)Busy(false);}
        }
        list.SelectionChanged+=(_,e)=>
        {
            if(changing)return;
            UpdateState();
        };
        search.TextChanged+=(_,_)=>Filter();
        title.TextChanged+=(_,_)=>{titleDisplay.Text=title.Text;UpdateState();};brief.TextChanged+=(_,_)=>UpdateState();
        entryInput.TextChanged+=async(_,_)=>
        {
            if(changing)return;
            draftContent=NotepadContent.NormalizeContent(entryInput.Text);UpdateState();await RefreshRows(true);
        };
        void SelectAll(){changing=true;try{list.SelectedItems.Clear();foreach(var row in list.Items.Cast<NotepadDisplayRow>().Where(row=>!row.IsChapter))list.SelectedItems.Add(row);}finally{changing=false;}UpdateState();}
        selectAll.Click+=(_,_)=>SelectAll();clear.Click+=(_,_)=>{list.SelectedItems.Clear();UpdateState();};
        delete.Click+=async(_,_)=>await Remove(SelectedWords());
        list.PreviewKeyDown+=async(_,e)=>
        {
            if(e.Key==Key.Delete&&(Keyboard.Modifiers&(ModifierKeys.Alt|ModifierKeys.Windows))==ModifierKeys.None){e.Handled=true;await Remove(SelectedWords());}
            else if(e.Key==Key.A&&Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;SelectAll();}
        };
        async Task Lookup(NotepadDisplayRow row)
        {
            if(lookup==null||row.IsChapter)return;
            if(Dirty()){status.Text="有未保存的修改，请先保存或撤销后再查词。";return;}
            dialog.Close();await lookup(row.Word);
        }
        list.MouseDoubleClick+=async(_,e)=>
        {
            if(e.ChangedButton!=MouseButton.Left||Keyboard.Modifiers!=ModifierKeys.None)return;
            var source=e.OriginalSource as DependencyObject;
            var container=source==null?null:ItemsControl.ContainerFromElement(list,source) as ListBoxItem;
            for(var element=source;element!=null&&element!=container;element=element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element))if(element is Button)return;
            if(container?.DataContext is NotepadDisplayRow row){e.Handled=true;await Lookup(row);}
        };
        entryAction.Click+=async(_,_)=>
        {
            try
            {
                await SetContent(draftContent);status.Text="词表与分组已更新 · 保存后同步云端。";
                entryPanel.IsExpanded=false;UpdateState();
            }
            catch(ArgumentException error){status.Text=error.Message;}
        };
        list.AddHandler(Button.ClickEvent,new RoutedEventHandler(async(_,e)=>
        {
            if(e.OriginalSource is not Button {DataContext:NotepadDisplayRow row} button||row.IsChapter)return;
            if(Equals(button.Tag,"remove")){await Remove(new[]{row.Word});return;}
            await Lookup(row);
        }));
        void Busy(bool value){busy=value;tabs.IsEnabled=close.IsEnabled=reload.IsEnabled=!value;UpdateState();}
        async Task Reset(CloudNotepad pad)
        {
            original=pad;draftContent=pad.Content;
            changing=true;try{title.Text=pad.Title;brief.Text=pad.Brief;entryInput.Text=draftContent;titleDisplay.Text=pad.Title;}finally{changing=false;}
            tagsInfo.Text="标签："+(pad.Tags.Count==0?"无":string.Join("、",pad.Tags));publicationInfo.Text="发布状态："+StatusLabel(pad.Status);
            list.SelectedItems.Clear();await RefreshRows();UpdateState();
        }
        undo.Click+=async(_,_)=>{await Reset(original);status.Text="已撤销尚未保存的修改。";};
        reload.Click+=async(_,_)=>
        {
            Busy(true);status.Text="正在读取云端词本…";
            try{var pad=await api.GetNotepadAsync(original.Id);await Reset(pad);saved(pad);status.Text="已载入云端最新内容。";}
            catch(Exception error){status.Text=error is MaiMemoException?error.Message:"读取失败，请稍后重试。";}
            finally{Busy(false);}
        };
        save.Click+=async(_,_)=>
        {
            if(NotepadContent.Words(draftContent).Count==0)
            {
                await DeleteBook();
                if(!closed){var message=status.Text;await SetContent(lastNonEmptyContent);status.Text=message+" 已恢复非空词表。";}
                return;
            }
            Busy(true);status.Text="正在保存到云端…";
            try{var pad=await api.UpdateNotepadAsync(original,title.Text,brief.Text,draftContent);await Reset(pad);saved(pad);status.Text="已保存到云词本，收藏状态已同步。";}
            catch(Exception error){status.Text=error is MaiMemoException or ArgumentException?error.Message:"保存未完成，修改仍保留，请重新载入确认。";}
            finally{Busy(false);}
        };
        close.Click+=(_,_)=>dialog.Close();dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        dialog.Closed+=(_,_)=>{closed=true;lifetime.Cancel();rowsCts?.Cancel();};
        _=RefreshRows();UpdateState();return dialog;
    }
    private static DataTemplate WordTemplate()
    {
        var panel=new FrameworkElementFactory(typeof(DockPanel));panel.SetValue(FrameworkElement.MarginProperty,new Thickness(0,2,0,2));
        var heading=new FrameworkElementFactory(typeof(TextBlock)){Name="ChapterTitle"};heading.SetBinding(TextBlock.TextProperty,new Binding("Word"));heading.SetValue(UIElement.VisibilityProperty,Visibility.Collapsed);heading.SetValue(DockPanel.DockProperty,Dock.Top);heading.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);heading.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);heading.SetValue(FrameworkElement.MarginProperty,new Thickness(2,7,0,4));panel.AppendChild(heading);
        var remove=new FrameworkElementFactory(typeof(Button)){Name="RemoveWord"};remove.SetValue(DockPanel.DockProperty,Dock.Right);remove.SetValue(ContentControl.ContentProperty,"×");remove.SetValue(FrameworkElement.TagProperty,"remove");remove.SetValue(FrameworkElement.WidthProperty,27d);remove.SetValue(Control.PaddingProperty,new Thickness(2));remove.SetValue(Control.BorderThicknessProperty,new Thickness(0));remove.SetValue(Control.BackgroundProperty,Brushes.Transparent);remove.SetValue(FrameworkElement.ToolTipProperty,"从列表删除这个单词");panel.AppendChild(remove);
        var lookup=new FrameworkElementFactory(typeof(Button)){Name="LookupWord"};lookup.SetValue(DockPanel.DockProperty,Dock.Right);lookup.SetValue(ContentControl.ContentProperty,"\uE721");lookup.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe MDL2 Assets"));lookup.SetValue(FrameworkElement.TagProperty,"lookup");lookup.SetValue(FrameworkElement.WidthProperty,27d);lookup.SetValue(Control.PaddingProperty,new Thickness(2));lookup.SetValue(Control.BorderThicknessProperty,new Thickness(0));lookup.SetValue(Control.BackgroundProperty,Brushes.Transparent);lookup.SetValue(FrameworkElement.ToolTipProperty,"查看释义（也可双击该行）");panel.AppendChild(lookup);
        var word=new FrameworkElementFactory(typeof(TextBlock)){Name="WordText"};word.SetValue(DockPanel.DockProperty,Dock.Left);word.SetValue(FrameworkElement.WidthProperty,148d);word.SetValue(FrameworkElement.MarginProperty,new Thickness(4,5,8,5));word.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);word.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);word.SetValue(TextBlock.ForegroundProperty,new SolidColorBrush(Color.FromRgb(8,127,175)));word.SetBinding(TextBlock.TextProperty,new Binding("Word"));panel.AppendChild(word);
        var meaning=new FrameworkElementFactory(typeof(TextBlock)){Name="MeaningLabel"};meaning.SetBinding(TextBlock.TextProperty,new Binding("Meaning"));meaning.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);meaning.SetValue(TextBlock.FontSizeProperty,12d);meaning.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);panel.AppendChild(meaning);
        var template=new DataTemplate(typeof(NotepadDisplayRow)){VisualTree=panel};var chapter=new DataTrigger {Binding=new Binding("IsChapter"),Value=true};
        foreach(var name in new[]{"LookupWord","RemoveWord","WordText","MeaningLabel"})chapter.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed,name));
        chapter.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible,"ChapterTitle"));template.Triggers.Add(chapter);return template;
    }
}
