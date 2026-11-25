using System.Text.RegularExpressions;

namespace MemoLex.Core;

public sealed record MnemonicPart(string Form, string Meaning);
public sealed record MnemonicBreakdown(IReadOnlyList<MnemonicPart> Parts, string Explanation, string? Word);

public static partial class MnemonicParts
{
    // Match authored constructions such as re-（再次）＋view（看）→回头再看.
    // Isolated examples and ordinary parenthetical notes are left as plain text.
    [GeneratedRegex(@"^(?<form>-?[a-z]+(?:\([a-z]+\))?[a-z]*-?)(?:\s*(?:（(?<meaning>.+)）|\((?<meaning>.+)\))|(?:\s+|\s*[：:]\s*)(?<meaning>[\p{IsCJKUnifiedIdeographs}].*))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabeledPart();

    [GeneratedRegex(@"^-?[a-z]+(?:\([a-z]+\))?[a-z]*-?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnlabeledPart();

    public static IReadOnlyList<MnemonicBreakdown> Parse(string word, string? type, string text)
    {
        if (MnemonicTypes.Normalize(type) is not ("词根词缀" or "派生")) return [];
        var result = new List<MnemonicBreakdown>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var arrow = line.IndexOf('→');
            var equals = line.IndexOfAny(['=', '＝']);
            var boundary = arrow < 0 ? equals : equals < 0 ? arrow : Math.Min(arrow, equals);
            if (boundary < 0) continue;
            var pieces = line[..boundary].Split(['+', '＋'], StringSplitOptions.TrimEntries);
            if (pieces.Length is < 2 or > 8) continue;

            var parts = new List<MnemonicPart>(pieces.Length);
            foreach (var piece in pieces)
            {
                var match = LabeledPart().Match(piece);
                if (match.Success && match.Groups["meaning"].Value.Trim() is { Length: > 0 } meaning)
                    parts.Add(new(match.Groups["form"].Value, meaning));
                else if (UnlabeledPart().IsMatch(piece))
                    parts.Add(new(piece, ""));
            }
            if (parts.Count == pieces.Length && parts.Count(part => part.Meaning.Length > 0) >= 2
                && line[(boundary + 1)..].Trim() is { Length: > 0 } explanation)
            {
                var combined=string.Concat(parts.Select(part=>part.Form.Replace("-","").Replace("(","").Replace(")","")));
                result.Add(new(parts,explanation,combined.Equals(word,StringComparison.OrdinalIgnoreCase)?word:null));
            }
            if (result.Count == 3) break;
        }
        return result;
    }
}
