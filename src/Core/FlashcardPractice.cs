namespace MemoLex.Core;

public enum FlashcardAnswer { Known, Vague, Forgotten }

public sealed class FlashcardPractice
{
    public const int GroupSize = 10;
    private readonly string[] _words;
    private readonly List<string> _queue = [];

    public FlashcardPractice(IEnumerable<string> words)
    {
        _words = words.Where(word => !string.IsNullOrWhiteSpace(word))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_words.Length > 0) StartNextGroup();
    }

    public int GroupNumber { get; private set; }
    public int GroupCount => (_words.Length + GroupSize - 1) / GroupSize;
    public int GroupWordCount { get; private set; }
    public int Remaining => _queue.Count;
    public string? Current => _queue.Count == 0 ? null : _queue[0];
    public bool HasNextGroup => GroupNumber < GroupCount;

    public bool StartNextGroup()
    {
        if (Remaining != 0 || !HasNextGroup) return false;
        _queue.AddRange(_words.Skip(GroupNumber * GroupSize).Take(GroupSize));
        GroupNumber++;
        GroupWordCount = Remaining;
        return true;
    }

    public void Answer(FlashcardAnswer answer)
    {
        if (Current is not { } word) return;
        _queue.RemoveAt(0);
        switch (answer)
        {
            case FlashcardAnswer.Vague: _queue.Insert(Math.Min(3, Remaining), word); break;
            case FlashcardAnswer.Forgotten: _queue.Add(word); break;
        }
    }
}
