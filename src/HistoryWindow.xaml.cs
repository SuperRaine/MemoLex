using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemoLex.Core;

namespace MemoLex;

public partial class HistoryWindow : Window
{
    private void CloseHistoryButton_Click(object sender, RoutedEventArgs e) => Close();
    private readonly Preferences _preferences;
    public string? SelectedWord { get; private set; }
    public HistoryWindow(Window owner, Preferences preferences)
    {
        _preferences = preferences;
        InitializeComponent();
        RoundedWindow.Attach(this);
        Owner = owner;
        MaxHeight = Math.Max(380, SystemParameters.WorkArea.Height - 50);
        RefreshEntries();
        Loaded += (_, _) => FilterBox.Focus();
    }
    private void RefreshEntries()
    {
        if (EntriesList == null) return;
        var filter = FilterBox.Text.Trim();
        var items = _preferences.SearchHistory.Where(item => item.Word.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        EntriesList.ItemsSource = items;
        CountText.Text = $"{_preferences.SearchHistory.Count} 个单词 · 最近查询在前 · 最多保留 {Preferences.HistoryLimit} 个";
        EmptyText.Text = _preferences.SearchHistory.Count == 0 ? "还没有搜词记录，查询单词后会自动保存。" : "没有匹配的历史记录。";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = _preferences.SearchHistory.Count > 0;
        DeleteButton.IsEnabled = LookupButton.IsEnabled = false;
    }
    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshEntries();
    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LookupButton != null) DeleteButton.IsEnabled = LookupButton.IsEnabled = EntriesList.SelectedItem is SearchHistoryEntry;
    }
    private void LookupButton_Click(object sender, RoutedEventArgs e) => ChooseWord();
    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) is ListViewItem) ChooseWord();
    }
    private void EntriesList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; ChooseWord(); }
        else if (e.Key == Key.Delete) { e.Handled = true; DeleteSelected(); }
    }
    private void ChooseWord()
    {
        if (EntriesList.SelectedItem is not SearchHistoryEntry item) return;
        SelectedWord = item.Word;
        DialogResult = true;
    }
    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void DeleteSelected()
    {
        if (EntriesList.SelectedItem is not SearchHistoryEntry item) return;
        _preferences.RemoveLookup(item.Word);
        _preferences.Save();
        RefreshEntries();
    }
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"清空全部 {_preferences.SearchHistory.Count} 条查词历史？", "清空查词历史", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _preferences.ClearHistory();
        _preferences.Save();
        RefreshEntries();
    }
}
