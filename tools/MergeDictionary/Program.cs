using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MemoLex.Build;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: MergeDictionary <base-data-directory> <preferred-file> <output-data-directory>");
    return 2;
}

try
{
    var preferred = new LocalBriefDictionary(args[1]);
    await preferred.LoadAsync();
    if (!preferred.Available) throw new InvalidDataException(preferred.LoadWarning);
    new DictionaryMerger().Merge(args[0], args[2], preferred.EnumerateEntries());
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

internal sealed class DictionaryMerger
{
    private const string WordIndex = "dictionary.index.tsv";
    private const string WordData = "dictionary.data";
    private const string WordMetadata = "dictionary.meta.json";
    private const string ChinesePostings = "chinese.postings";
    private const string ChineseTokens = "chinese.tokens.tsv";
    private const string ChineseMetadata = "chinese.meta.json";
    private const string PreferredSource = "local-brief";
    private static readonly string[] RebuiltFiles = [WordIndex, WordData, WordMetadata, ChinesePostings, ChineseTokens, ChineseMetadata];
    private static readonly Regex EnglishSpelling = new(@"\A[A-Za-z][A-Za-z .’'\-]{0,99}\z", RegexOptions.Compiled);
    private static readonly Regex Annotations = new(@"\[[^\]]*\]|\([^)]*\)|（[^）]*）", RegexOptions.Compiled);
    private static readonly Regex HanChunks = new(@"[\u3400-\u9fff]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOutput = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly Dictionary<uint, List<int>> _postings = [];
    private readonly HashSet<uint> _grams = [];
    private int _rows;
    private int _indexedRows;

    public void Merge(string baseDirectory, string outputDirectory, IEnumerable<(string Word, string Translation)> entries)
    {
        baseDirectory = Path.GetFullPath(baseDirectory);
        outputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("Use a new output directory for the merged dictionary.");

        var dictionaryMetadata = ReadMetadata(baseDirectory, WordMetadata);
        var chineseMetadata = ReadMetadata(baseDirectory, ChineseMetadata);
        if (!Sha256(Path.Combine(baseDirectory, WordIndex)).Equals(
                chineseMetadata["dictionary_index_sha256"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Chinese synonym index does not match the base dictionary.");
        if (!File.Exists(Path.Combine(baseDirectory, "chinese.synonyms.json")))
            throw new FileNotFoundException("The base dictionary must include chinese.synonyms.json.");

        var preferred = new Dictionary<string, (string Word, string Translation)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            if (!string.IsNullOrWhiteSpace(entry.Translation)) preferred.Add(entry.Word, entry);
        if (preferred.Count == 0) throw new InvalidDataException("The preferred dictionary has no definitions.");

        var otherFiles = Directory.EnumerateFiles(baseDirectory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Select(path => Path.GetRelativePath(baseDirectory, path))
            .Where(path => !RebuiltFiles.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray();
        var parent = Path.GetDirectoryName(outputDirectory)!;
        var staging = Path.Combine(parent, ".dictionary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var (baseRows, added) = WriteDictionary(baseDirectory, staging, preferred, chineseMetadata["dictionary_rows"]!.GetValue<int>());
            WriteChineseIndex(staging);

            dictionaryMetadata["base_entries"] = baseRows;
            dictionaryMetadata["entries"] = _rows;
            dictionaryMetadata["preferred_entries"] = preferred.Count;
            dictionaryMetadata["license"] = "The base ECDICT data is MIT (see ECDICT-LICENSE.txt).";
            dictionaryMetadata["record_fields"]?.AsArray().Add("source");
            chineseMetadata["dictionary_rows"] = _rows;
            chineseMetadata["indexed_entries"] = _indexedRows;
            chineseMetadata["tokens"] = _postings.Count;
            chineseMetadata["dictionary_index_sha256"] = Sha256(Path.Combine(staging, WordIndex));
            File.WriteAllText(Path.Combine(staging, WordMetadata), dictionaryMetadata.ToJsonString());
            File.WriteAllText(Path.Combine(staging, ChineseMetadata), chineseMetadata.ToJsonString());

            foreach (var file in otherFiles)
            {
                var target = Path.Combine(staging, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(baseDirectory, file), target);
            }

            Directory.Move(staging, outputDirectory);
            Console.WriteLine($"Merged {_rows:N0} rows. Replaced {preferred.Count - added:N0} definitions and added {added:N0} terms.");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private (int BaseRows, int Added) WriteDictionary(string source, string destination,
        Dictionary<string, (string Word, string Translation)> preferred, int expectedBaseRows)
    {
        using var sourceData = File.OpenRead(Path.Combine(source, WordData));
        using var data = File.Create(Path.Combine(destination, WordData));
        using var index = File.CreateText(Path.Combine(destination, WordIndex));
        index.NewLine = "\n";
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[4096];

        // Keep the original row IDs used in the Chinese synonym table.
        foreach (var line in File.ReadLines(Path.Combine(source, WordIndex)))
        {
            var fields = line.Split('\t');
            if (fields.Length != 3 || !long.TryParse(fields[1], CultureInfo.InvariantCulture, out var offset)
                || !int.TryParse(fields[2], CultureInfo.InvariantCulture, out var length)
                || length <= 0 || offset < 0 || offset > sourceData.Length - length)
                throw new InvalidDataException($"Invalid dictionary index row {_rows + 1}.");

            var word = fields[0];
            if (length > buffer.Length) Array.Resize(ref buffer, length);
            sourceData.Position = offset;
            sourceData.ReadExactly(buffer.AsSpan(0, length));
            if (preferred.TryGetValue(word, out var entry))
            {
                matched.Add(word);
                var row = JsonNode.Parse(buffer.AsSpan(0, length))!.AsObject();
                row["translation"] = entry.Translation;
                row["source"] = PreferredSource;
                WriteRow(data, index, word, JsonSerializer.SerializeToUtf8Bytes(row, JsonOutput));
                IndexChinese(entry.Translation, _rows - 1);
            }
            else
            {
                WriteRow(data, index, word, buffer.AsSpan(0, length));
                if (!EnglishSpelling.IsMatch(word)) continue;
                using var row = JsonDocument.Parse(buffer.AsMemory(0, length));
                if (row.RootElement.TryGetProperty("translation", out var translation) && translation.GetString() is { } meaning)
                    IndexChinese(meaning, _rows - 1);
            }
        }
        var baseRows = _rows;
        if (baseRows != expectedBaseRows) throw new InvalidDataException("The base dictionary row count has changed.");
        foreach (var entry in preferred.Values.OrderBy(entry => entry.Word, StringComparer.OrdinalIgnoreCase))
        {
            if (matched.Contains(entry.Word)) continue;
            var row = new { word = entry.Word, translation = entry.Translation, source = PreferredSource };
            WriteRow(data, index, entry.Word, JsonSerializer.SerializeToUtf8Bytes(row, JsonOutput));
            IndexChinese(entry.Translation, _rows - 1);
        }
        return (baseRows, _rows - baseRows);
    }

    private void WriteRow(FileStream data, StreamWriter index, string word, ReadOnlySpan<byte> bytes)
    {
        index.WriteLine(FormattableString.Invariant($"{word}\t{data.Position}\t{bytes.Length}"));
        data.Write(bytes);
        _rows++;
    }

    private void IndexChinese(string translation, int row)
    {
        _grams.Clear();
        foreach (Match chunk in HanChunks.Matches(Annotations.Replace(translation, "")))
        {
            var letters = chunk.ValueSpan;
            for (var i = 0; i < letters.Length; i++)
            {
                _grams.Add(letters[i]);
                if (i > 0) _grams.Add(((uint)letters[i - 1] << 16) | letters[i]);
            }
        }
        if (_grams.Count == 0) return;
        foreach (var gram in _grams)
        {
            if (!_postings.TryGetValue(gram, out var ids)) _postings[gram] = ids = [];
            ids.Add(row);
        }
        _indexedRows++;
    }

    private void WriteChineseIndex(string destination)
    {
        using var file = File.Create(Path.Combine(destination, ChinesePostings));
        using var binary = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
        using var index = File.CreateText(Path.Combine(destination, ChineseTokens));
        index.NewLine = "\n";
        binary.Write("CJX1"u8);
        binary.Write(_rows);
        foreach (var (gram, ids) in _postings.OrderBy(pair => pair.Key))
        {
            var offset = file.Position;
            var previous = 0;
            foreach (var id in ids)
            {
                binary.Write7BitEncodedInt(id - previous);
                previous = id;
            }
            var token = gram >> 16 == 0 ? $"1:{(char)gram}" : $"2:{(char)(gram >> 16)}{(char)gram}";
            index.WriteLine(FormattableString.Invariant($"{token}\t{offset}\t{file.Position - offset}"));
        }
    }

    private static JsonObject ReadMetadata(string directory, string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name)))!.AsObject();

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
