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
        SynonymContrastButton.Visibility = Visibility.Collapsed;
        SimilarContrastButton.Visibility = Visibility.Collapsed;

        WordRelations? related;
        try { related = await _related.LookupAsync(word, cancellation); }
        catch (OperationCanceledException) { return; }
        catch { related = null; }
        if (version != _wordVersion || cancellation.IsCancellationRequested) return;

        SynonymContrastButton.Visibility = related?.Synonyms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SimilarContrastButton.Visibility = related?.Similar.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Render(SynonymsPanel, SynonymsToggle, "近义词", related?.Synonyms, contrastMode: "synonym");
        Render(SimilarPanel, SimilarToggle, "形近词", related?.Similar, contrastMode: "similar");
        Render(DerivativesPanel, DerivativesToggle, "派生词", related?.Derivatives, contrastMode: null);
        Render(PhrasesPanel, PhrasesToggle, "常用短语", related?.Phrases, contrastMode: null);

        void Render(StackPanel panel, ToggleButton toggle, string title, IReadOnlyList<RelatedWord>? items, string? contrastMode)
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
            Grid.SetIsSharedSizeScope(panel, true);
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var row = new Grid();
                // Use the same inset for every group while leaving room for the definition on narrow windows.
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 166 });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.25, GridUnitType.Star) });
                if (contrastMode != null)
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var headword = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 165,
                    FontSize = 13, LineHeight = 21, Margin = new Thickness(0, 0, 16, 0) };
                var link = new Hyperlink(new Run(item.Word)) { Foreground = Brush("#087FAF"), TextDecorations = null,
                    FontWeight = FontWeights.SemiBold, ToolTip = "查询 " + item.Word, ContextMenu = HaiciWordMenu(item.Word) };
                link.Click += async (_, _) => await NavigateLinkedWordAsync(item.Word, word);
                headword.Inlines.Add(link);
                row.Children.Add(headword);

                var meaning = new StackPanel();
                foreach (var (partOfSpeech, text) in item.Senses)
                {
                    var sense = new Grid();
                    sense.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "PartOfSpeech" });
                    sense.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    if (partOfSpeech.Length > 0)
                    {
                        sense.Children.Add(new TextBlock { Text = partOfSpeech, FontSize = 12, LineHeight = 21,
                            Foreground = Brush("#627B90"), Margin = new Thickness(0, 0, 9, 0),
                            MaxWidth = 73, TextWrapping = TextWrapping.Wrap });
                    }
                    var definition = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
                        FontSize = 13, LineHeight = 21, Foreground = Brush("#355771") };
                    Grid.SetColumn(definition, 1);
                    sense.Children.Add(definition);
                    meaning.Children.Add(sense);
                }
                Grid.SetColumn(meaning, 1);
                row.Children.Add(meaning);

                if (contrastMode != null)
                {
                    var capturedWord = item.Word;
                    var capturedMode = contrastMode;
                    var contrastBtn = new Button
                    {
                        Content = capturedMode == "similar" ? "区分" : "辨析",
                        FontSize = 11,
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                        ToolTip = capturedMode == "similar"
                            ? $"将 {word} 与 {capturedWord} 进行形近区分"
                            : $"将 {word} 与 {capturedWord} 进行近义辨析"
                    };
                    if (TryFindResource("PillButton") is Style pillStyle) contrastBtn.Style = pillStyle;
                    contrastBtn.Click += async (_, _) => await OpenWordContrastAsync(word, capturedMode, capturedWord);
                    Grid.SetColumn(contrastBtn, 2);
                    row.Children.Add(contrastBtn);
                }

                panel.Children.Add(new Border { Child = row, Padding = new Thickness(0, 7, 0, 7),
                    BorderBrush = Brush("#E4EEF6"), BorderThickness = new Thickness(0, 0, 0, index == items.Count - 1 ? 0 : 1) });
            }
        }
    }

    private async void SynonymContrastButton_Click(object sender, RoutedEventArgs e)
    {
        if (ValidWord(_word)) await OpenWordContrastAsync(_word, "synonym");
    }

    private async void SimilarContrastButton_Click(object sender, RoutedEventArgs e)
    {
        if (ValidWord(_word)) await OpenWordContrastAsync(_word, "similar");
    }

    private async Task OpenWordContrastAsync(string word, string mode, string? focusWord = null)
    {
        if (!_ready || !ValidWord(word)) return;
        var report = await WordContrast.BuildAsync(word, mode, _dictionary, _related, _mnemonics, MnemonicAccountKey, focusWord);
        if (report == null) return;
        var dialog = WordContrastDialog.Build(
            this,
            report,
            lookupWord: w => _ = LoadWordAsync(w),
            speakWord: (w, accent) => PlaySpeechAsync(w, accent),
            focusWord: focusWord);
        dialog.ShowDialog();
    }
    private void RelationsExpansion_Changed(object sender,RoutedEventArgs e)=>RememberCurrentView();

    private void RelationDetails_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_navigating && e.NewValue is true && sender is Border { IsLoaded: true } details)
            UiMotion.Reveal(details, distance: 3);
    }
}
