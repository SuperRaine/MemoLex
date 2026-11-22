using System.ComponentModel;
using System.Windows.Controls;
using MemoLex.Services;

namespace MemoLex;

internal sealed class WordListRow(string word, string localMeaning, OnlineDictionaryClient? online,
    bool isChapter = false, bool conceal = false) : INotifyPropertyChanged
{
    private bool _conceal = conceal, _revealed, _attempted;
    private OnlineDefinition? _definition;

    public string Word { get; } = word;
    public bool IsChapter { get; } = isChapter;
    public string Meaning => _definition?.Meaning ?? localMeaning;
    public bool IsConcealed => _conceal && !_revealed;
    internal bool NeedsOnlineMeaning => online != null && !IsChapter && !_attempted && Meaning.Length == 0;
    public string DisplayMeaning => IsConcealed ? "点击查看释义"
        : Meaning.Length > 0 ? Meaning : NeedsOnlineMeaning ? "正在联网补查…" : "暂无释义";
    public string MeaningTooltip => !IsConcealed && _definition is { } found
        ? DisplayMeaning + "\n" + found.SourceLabel + (found.SourceAttribution is { } source ? "\n" + source : "")
        : DisplayMeaning;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal async Task LoadOnlineAsync(CancellationToken cancellation)
    {
        if (!NeedsOnlineMeaning || IsConcealed) return;
        var meaning = await online!.LookupAsync(Word, cancellation);
        cancellation.ThrowIfCancellationRequested();
        _definition = meaning;
        _attempted = true;
        PropertyChanged?.Invoke(this, new(nameof(Meaning)));
        UpdateDisplay();
    }

    internal void SetConceal(bool enabled)
    {
        _conceal = enabled;
        _revealed = false;
        ConcealChanged();
    }

    internal void ToggleMeaning()
    {
        if (!_conceal) return;
        _revealed = !_revealed;
        ConcealChanged();
    }

    private void ConcealChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(IsConcealed)));
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        PropertyChanged?.Invoke(this, new(nameof(DisplayMeaning)));
        PropertyChanged?.Invoke(this, new(nameof(MeaningTooltip)));
    }
}

public sealed class WordListMeaningText : TextBlock
{
    private WordListRow? _row;
    private CancellationTokenSource? _request;

    public WordListMeaningText()
    {
        Loaded += (_, _) => UpdateRow();
        Unloaded += (_, _) => SetRow(null);
        IsVisibleChanged += (_, _) => UpdateRow();
        DataContextChanged += (_, _) => UpdateRow();
    }

    private void UpdateRow() => SetRow(IsLoaded && IsVisible ? DataContext as WordListRow : null);

    private void SetRow(WordListRow? row)
    {
        if (ReferenceEquals(_row, row)) return;
        CancelRequest();
        if (_row != null) _row.PropertyChanged -= OnRowChanged;
        _row = row;
        if (_row == null) return;
        _row.PropertyChanged += OnRowChanged;
        LoadMeaning();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(WordListRow.IsConcealed)) return;
        if (_row?.IsConcealed == true) CancelRequest();
        else LoadMeaning();
    }

    private void CancelRequest()
    {
        _request?.Cancel();
        _request = null;
    }

    private async void LoadMeaning()
    {
        if (_request != null || _row is not { NeedsOnlineMeaning: true, IsConcealed: false } row) return;
        using var request = new CancellationTokenSource();
        _request = request;
        try { await row.LoadOnlineAsync(request.Token); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }
}
