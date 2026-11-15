using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using MemoLex.Core;

namespace MemoLex;

public partial class MainWindow
{
    private async Task LoadRelationsAsync(string word,int version,CancellationToken cancellation)
    {
        SynonymsPanel.Children.Clear();SimilarPanel.Children.Clear();DerivativesPanel.Children.Clear();PhrasesPanel.Children.Clear();
        RelatedHintText.Text="读取中…";
        SynonymsToggle.Content="近义词…";SimilarToggle.Content="形近词…";DerivativesToggle.Content="派生词…";PhrasesToggle.Content="常用短语…";
        try
        {
            var related=await _related.LookupAsync(word,cancellation);
            if(version!=_wordVersion||cancellation.IsCancellationRequested)return;
            void Render(StackPanel panel,ToggleButton toggle,string title,IReadOnlyList<RelatedWord> items)
            {
                toggle.Content=$"{title} · {items.Count}";
                if(items.Count==0){panel.Children.Add(new TextBlock {Text="暂未找到相关单词",FontSize=12,Foreground=Brush("#91A7BA")});return;}
                foreach(var item in items)
                {
                    var block=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=13,LineHeight=24,Margin=new Thickness(0,0,0,8),Foreground=Brush("#718EA5")};
                    var link=new Hyperlink(new Run(item.Word)){Foreground=Brush("#087FAF"),TextDecorations=null,FontWeight=FontWeights.SemiBold,ToolTip="查询 "+item.Word};
                    link.ContextMenu=HaiciWordMenu(item.Word);
            link.Click+=async(_,_)=>await NavigateLinkedWordAsync(item.Word,word);
                    block.Inlines.Add(link);block.Inlines.Add(new Run("  "+item.Meaning.Replace('\n',' ')));
                    panel.Children.Add(block);
                }
            }
            Render(SynonymsPanel,SynonymsToggle,"近义词",related.Synonyms);
            Render(SimilarPanel,SimilarToggle,"形近词",related.Similar);
            Render(DerivativesPanel,DerivativesToggle,"派生词",related.Derivatives);
            Render(PhrasesPanel,PhrasesToggle,"常用短语",related.Phrases);
            RelatedHintText.Text=_related.Available?"离线词库":"本地词组";
        }
        catch(OperationCanceledException) { }
        catch { if(version==_wordVersion)RelatedHintText.Text="未载入"; }
    }
    private void RelationsExpansion_Changed(object sender,RoutedEventArgs e)=>RememberCurrentView();
}
