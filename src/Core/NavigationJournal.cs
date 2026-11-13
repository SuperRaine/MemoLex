namespace MemoLex.Core;

/// <summary>A bounded browser-style journal. Updating a view never creates a new entry.</summary>
public sealed class NavigationJournal<T>(Func<T, T, bool> sameLocation, int capacity = 120) where T : class
{
    private readonly List<T> _entries = [];
    private readonly int _capacity = Math.Max(2, capacity);
    private int _index = -1;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;
    public T? Current => _index >= 0 ? _entries[_index] : null;
    public T? BackDestination => CanGoBack ? _entries[_index - 1] : null;
    public T? ForwardDestination => CanGoForward ? _entries[_index + 1] : null;
    public int Count => _entries.Count;
    public void UpdateCurrent(T state)
    {
        if (_index < 0) { Navigate(state); return; }
        _entries[_index] = state;
    }
    public void Navigate(T state)
    {
        if (Current is T current && sameLocation(current, state)) { _entries[_index] = state; return; }
        if (_index < _entries.Count - 1) _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(state);
        if (_entries.Count > _capacity) _entries.RemoveAt(0);
        _index = _entries.Count - 1;
    }
    public T? GoBack() => CanGoBack ? _entries[--_index] : null;
    public T? GoForward() => CanGoForward ? _entries[++_index] : null;
}
