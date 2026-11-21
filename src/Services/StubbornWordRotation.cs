namespace MemoLex.Services;

internal enum StubbornSortMode { Review, Alphabetical, Random }

internal sealed class StubbornWordRotation(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private int _position;
    internal IReadOnlyList<StubbornWord> Words { get; private set; } = [];
    internal StubbornSortMode Mode { get; private set; }
    internal StubbornWord? Current => Words.Count == 0 ? null : Words[_position];

    internal void Reset(IEnumerable<StubbornWord> words, StubbornSortMode mode, bool descending = false, bool preserveCurrent = false)
    {
        var unique = words.DistinctBy(word => word.Spelling, StringComparer.OrdinalIgnoreCase).ToArray();
        var previous = preserveCurrent ? Current?.Spelling : null;
        if (preserveCurrent && Mode == StubbornSortMode.Random && mode == Mode && Words.Count == unique.Length)
        {
            var updated = unique.ToDictionary(word => word.Spelling, StringComparer.OrdinalIgnoreCase);
            if (Words.All(word => updated.ContainsKey(word.Spelling)))
            {
                Words = Words.Select(word => updated[word.Spelling]).ToArray();
                return;
            }
        }
        Mode = mode;
        _position = 0;
        var ordered = mode switch
        {
            StubbornSortMode.Alphabetical when descending => unique.OrderByDescending(word => word.Spelling, StringComparer.OrdinalIgnoreCase).ToArray(),
            StubbornSortMode.Alphabetical => unique.OrderBy(word => word.Spelling, StringComparer.OrdinalIgnoreCase).ToArray(),
            StubbornSortMode.Random => Shuffled(unique, null),
            _ => unique.OrderBy(word => word.NextStudyDate ?? DateTimeOffset.MaxValue)
                .ThenBy(word => word.Spelling, StringComparer.OrdinalIgnoreCase).ToArray()
        };
        if (previous != null)
        {
            var index = Array.FindIndex(ordered, word => word.Spelling.Equals(previous, StringComparison.OrdinalIgnoreCase));
            if (index > 0 && mode == StubbornSortMode.Random)
                (ordered[0], ordered[index]) = (ordered[index], ordered[0]);
            else _position = Math.Max(0, index);
        }
        Words = ordered;
    }

    internal void Advance()
    {
        if (Words.Count < 2) return;
        var previous = Current!.Spelling;
        if (++_position < Words.Count) return;
        _position = 0;
        if (Mode == StubbornSortMode.Random) Words = Shuffled(Words, previous);
    }

    private StubbornWord[] Shuffled(IEnumerable<StubbornWord> words, string? avoidFirst)
    {
        var result = words.ToArray();
        _random.Shuffle(result);
        if (result.Length > 1 && result[0].Spelling.Equals(avoidFirst, StringComparison.OrdinalIgnoreCase))
        {
            var other = _random.Next(1, result.Length);
            (result[0], result[other]) = (result[other], result[0]);
        }
        return result;
    }
}
