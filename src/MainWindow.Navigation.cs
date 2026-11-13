using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private enum ViewTarget { Word, Examples, Translation, ChineseSearch }
    private sealed record ExampleSnapshot(string PaperType, ExampleSentence[] Items, int Offset, int Total, bool HaveMore, bool Ready, bool CanRetry, string Hint);
    private sealed record ViewState(string Word, ViewTarget Target, ExampleSnapshot Examples, double ScrollOffset,
        bool SynonymsOpen,bool SimilarOpen,bool DerivativesOpen,bool PhrasesOpen,TranslationSnapshot? Translation,ChineseSnapshot? Chinese);
    private readonly NavigationJournal<ViewState> _navigation = new((a,b) =>
        a.Target==b.Target && (a.Target==ViewTarget.Translation || (a.Target==ViewTarget.ChineseSearch ? a.Chinese?.Query==b.Chinese?.Query :
        (a.Word.Equals(b.Word, StringComparison.OrdinalIgnoreCase) && a.Examples.PaperType == b.Examples.PaperType))));
    private bool _navigating;
    private int _wordVersion;
    private ViewTarget _viewTarget;

    private ViewState CaptureView() => new(_word, _viewTarget,
        new ExampleSnapshot(_displayedPaperType, _shownExamples.ToArray(), _exampleOffset, _exampleTotal, _examplesHaveMore, _examplesLoaded, _examplesError, ExamplesHintText.Text),
        ResultScroll.VerticalOffset,SynonymsToggle.IsChecked==true,SimilarToggle.IsChecked==true,DerivativesToggle.IsChecked==true,PhrasesToggle.IsChecked==true,CaptureTranslation(),CaptureChinese());

    private void RememberCurrentView()
    {
        if (!_ready || _navigating || (_viewTarget is not (ViewTarget.Translation or ViewTarget.ChineseSearch)&&!ValidWord(_word))) return;
        _navigation.UpdateCurrent(CaptureView());
    }
    private void ResultScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange!=0&&ReferenceEquals(e.Source, ResultScroll)&&_viewTarget is ViewTarget.Word or ViewTarget.Examples) RememberCurrentView();
    }
    private void UpdateHeader() => HeaderWordText.Text = _viewTarget==ViewTarget.Translation?"段落翻译":_viewTarget==ViewTarget.ChineseSearch?"中文反查 · "+_chineseQuery:_word;
    private void QueueViewRestore(int version, Action restore) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
        new Action(() => { if (version == _wordVersion) restore(); }));
    private void QueueExamplesScroll(int wordVersion, int exampleVersion) => QueueViewRestore(wordVersion, () =>
    {
        if (exampleVersion != _exampleVersion) return;
        ResultScroll.UpdateLayout();
        var position = ExamplesSection.TranslatePoint(new Point(), (UIElement)ResultScroll.Content);
        ResultScroll.ScrollToVerticalOffset(Math.Max(0, position.Y - 8));
    });
    private static string LocationLabel(ViewState state) => state.Target==ViewTarget.Translation?"段落翻译":state.Target==ViewTarget.ChineseSearch?"中文 · "+state.Chinese?.Query:state.Target == ViewTarget.Examples ? state.Word + " · 例句" : state.Word;
    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = _navigation.CanGoBack;
        ForwardButton.IsEnabled = _navigation.CanGoForward;
        BackButton.ToolTip = _navigation.BackDestination is { } back ? $"后退到 {LocationLabel(back)}（{ShortcutLabel("back")}）" : $"后退（{ShortcutLabel("back")}）";
        ForwardButton.ToolTip = _navigation.ForwardDestination is { } forward ? $"前进到 {LocationLabel(forward)}（{ShortcutLabel("forward")}）" : $"前进（{ShortcutLabel("forward")}）";
    }
    private async Task MoveNavigationAsync(int direction)
    {
        RememberCurrentView();
        var destination = direction < 0 ? _navigation.GoBack() : _navigation.GoForward();
        if (destination == null) return;
        if(destination.Target==ViewTarget.Translation){RestoreTranslationView(destination);return;}
        if(destination.Target==ViewTarget.ChineseSearch&&destination.Chinese is { } chinese){await SearchChineseAsync(chinese.Query,false,false,chinese);return;}
        await LoadWordAsync(destination.Word, recordHistory: false, addNavigationEntry: false, restore: destination);
    }
    private async void BackButton_Click(object sender, RoutedEventArgs e) => await MoveNavigationAsync(-1);
    private async void ForwardButton_Click(object sender, RoutedEventArgs e) => await MoveNavigationAsync(1);
    private async void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2)) return;
        e.Handled = true;
        await MoveNavigationAsync(e.ChangedButton == MouseButton.XButton1 ? -1 : 1);
    }
    private async Task OpenExamExamplesAsync(string paperType)
    {
        _changingExampleFilter = true;
        ExamplePaperCombo.SelectedItem = PaperTypeChoice.All.First(type => type.Value == paperType);
        _changingExampleFilter = false;
        _preferences.ExamplePaperType = paperType;
        _preferences.Save();
        await LoadExamplesAsync(reset: true, recordFilterNavigation: true);
    }
}
