using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using MemoLex.Core;
using MemoLex.Desktop;

namespace MemoLex;

public partial class MainWindow
{
    private async Task LoadRelationsAsync(string word,int version,CancellationToken cancellation)
    {
        SynonymsPanel.Children.Clear();SimilarPanel.Children.Clear();DerivativesPanel.Children.Clear();PhrasesPanel.Children.Clear();
        SynonymsToggle.Content="近义词…";SimilarToggle.Content="形近词…";DerivativesToggle.Content="派生词…";PhrasesToggle.Content="常用短语…";

        WordRelations? related;
        try { related = await _related.LookupAsync(word, cancellation); }
        catch (OperationCanceledException) { return; }
        catch { related = null; }
        if (version != _wordVersion || cancellation.IsCancellationRequested) return;

        Render(SynonymsPanel, SynonymsToggle, "近义词", related?.Synonyms);
        Render(SimilarPanel, SimilarToggle, "形近词", related?.Similar);
        Render(DerivativesPanel, DerivativesToggle, "派生词", related?.Derivatives);
        Render(PhrasesPanel, PhrasesToggle, "常用短语", related?.Phrases);

        void Render(StackPanel panel, ToggleButton toggle, string title, IReadOnlyList<RelatedWord>? items)
        {
            toggle.Content = items == null ? title : $"{title} · {items.Count}";
            if (items == null || items.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = items == null ? "未能加载相关词，请重试。" : "暂无相关词。",
                    FontSize = 12, Foreground = Brush("#91A7BA")
                });
                return;
            }
            foreach (var item in items)
            {
                var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 24,
                    Margin = new Thickness(0, 0, 0, 8), Foreground = Brush("#718EA5") };
                var link = new Hyperlink(new Run(item.Word)) { Foreground = Brush("#087FAF"), TextDecorations = null,
                    FontWeight = FontWeights.SemiBold, ToolTip = "查询 " + item.Word, ContextMenu = HaiciWordMenu(item.Word) };
                link.Click += async (_, _) => await NavigateLinkedWordAsync(item.Word, word);
                block.Inlines.Add(link);
                block.Inlines.Add(new Run("  " + item.Meaning));
                panel.Children.Add(block);
            }
        }
    }
    private void RelationsExpansion_Changed(object sender,RoutedEventArgs e)=>RememberCurrentView();

    private void RelationDetails_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_navigating && e.NewValue is true && sender is Border { IsLoaded: true } details)
            UiMotion.Reveal(details, distance: 3);
    }
}
