using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MemoLex.Core;

/// <summary>
/// Reads the plaintext brief-definition section of one verified local file format.
/// The source stays outside MemoLex; no dictionary data is exported or written.
/// </summary>
public sealed class LocalBriefDictionary
{
    public const string SourceLabel = "本地简明释义";

    // This is a format profile, not a general-purpose combined.bin decoder. Unknown
    // releases must be inspected independently before adding another profile.
    private static readonly FormatProfile SupportedProfile = new(
        98_105_014,
        "6dc57413125672017fa6fa3104a600934e38a1c51368ff3b3adc015cb7ad86de",
        "5611B0000000936C010000000000340C05000000",
        76_761_050,
        13_359_209,
        268_787);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] ForvoMarker = "<FORVO/>"u8.ToArray();
    private readonly string _path;
    private readonly FormatProfile _profile;
    private readonly object _loadGate = new();
    private Task? _loadTask;
    private LoadedState? _state;
    private volatile string? _loadWarning;

    public LocalBriefDictionary(string path) : this(path, SupportedProfile) { }

    internal LocalBriefDictionary(string path, FormatProfile profile)
    {
        _path = path;
        _profile = profile;
    }

    public int Count => Volatile.Read(ref _state)?.Entries.Count ?? 0;
    public bool Available => Volatile.Read(ref _state) is not null;
    public string? LoadWarning => _loadWarning;

    /// <summary>Loads once; missing, changed, or unsupported files leave the reader unavailable.</summary>
    public Task LoadAsync()
    {
        lock (_loadGate) return _loadTask ??= Task.Run(LoadCore);
    }

    public DictionaryEntry? Lookup(string word)
    {
        var state = Volatile.Read(ref _state);
        if (state is null || string.IsNullOrWhiteSpace(word)
            || !state.Entries.TryGetValue(word.Trim(), out var address)) return null;

        return new DictionaryEntry
        {
            word = address.Word,
            translation = StrictUtf8.GetString(state.Text, address.Offset, address.Length)
        };
    }

    public bool TryLookup(string word, out DictionaryEntry? entry)
    {
        entry = Lookup(word);
        return entry is not null;
    }

    private void LoadCore()
    {
        try
        {
            var header = ValidateProfile(_profile);
            using var source = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 131_072, FileOptions.SequentialScan);
            if (source.Length != _profile.FileLength) throw new InvalidDataException();

            var actualHeader = new byte[header.Length];
            source.ReadExactly(actualHeader);
            if (!actualHeader.AsSpan().SequenceEqual(header)) throw new InvalidDataException();

            // The hash pins offsets to the inspected snapshot and also rejects changes
            // outside the brief section. FileShare.Read prevents concurrent mutation.
            source.Position = 0;
            var digest = SHA256.HashData(source);
            if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(_profile.Sha256)))
                throw new InvalidDataException();

            source.Position = _profile.TextOffset;
            var text = new byte[_profile.TextLength];
            source.ReadExactly(text);
            var entries = ParsePlaintextSection(text, _profile.EntryCount);
            Volatile.Write(ref _state, new LoadedState(text, entries));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _loadWarning = "未找到本地词库。";
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or FormatException)
        {
            _loadWarning = "本地词库版本不受支持，或文件不完整。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            _loadWarning = "暂时无法读取本地词库。";
        }
    }

    private static byte[] ValidateProfile(FormatProfile profile)
    {
        if (profile.FileLength <= 0 || profile.FileLength > 512L * 1024 * 1024
            || profile.TextOffset < 0 || profile.TextLength <= 0 || profile.TextLength > 64 * 1024 * 1024
            || profile.TextOffset > profile.FileLength - profile.TextLength
            || profile.EntryCount <= 0 || profile.EntryCount > 1_000_000
            || profile.EntryCount > profile.TextLength / 11 || profile.Sha256.Length != 64
            || profile.HeaderHex.Length is < 2 or > 8192)
            throw new InvalidDataException();

        var header = Convert.FromHexString(profile.HeaderHex);
        if (profile.TextOffset < header.Length || Convert.FromHexString(profile.Sha256).Length != 32)
            throw new InvalidDataException();
        return header;
    }

    internal static Dictionary<string, EntryAddress> ParsePlaintextSection(byte[] text, int expectedCount)
    {
        if (expectedCount <= 0 || expectedCount > 1_000_000 || expectedCount > text.Length / 11)
            throw new InvalidDataException();

        var entries = new Dictionary<string, EntryAddress>(expectedCount, StringComparer.OrdinalIgnoreCase);
        var cursor = 0;
        for (var id = 0; id < expectedCount; id++)
        {
            var wordRange = ReadCString(text, ref cursor, 256);
            var word = StrictUtf8.GetString(text, wordRange.Offset, wordRange.Length);
            if (word.Length == 0 || word.Any(char.IsControl) || word != word.Trim()
                || cursor > text.Length - 8) throw new InvalidDataException();

            // The first UInt32 is metadata not needed for brief lookup. The second
            // is the record ordinal, which must be consecutive across this section.
            if (BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(cursor + 4, 4)) != (uint)id)
                throw new InvalidDataException();
            cursor += 8;
            var definitionRange = ReadCString(text, ref cursor, 16_384);
            var definition = StrictUtf8.GetString(text, definitionRange.Offset, definitionRange.Length);
            if (definition.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
                throw new InvalidDataException();

            if (!entries.TryAdd(word, new EntryAddress(word, definitionRange.Offset, definitionRange.Length)))
                throw new InvalidDataException();
            if (text.AsSpan(cursor).StartsWith(ForvoMarker)) cursor += ForvoMarker.Length;
        }
        if (cursor != text.Length) throw new InvalidDataException();
        return entries;
    }

    private static (int Offset, int Length) ReadCString(byte[] text, ref int cursor, int maximumBytes)
    {
        if ((uint)cursor >= (uint)text.Length) throw new InvalidDataException();
        var scanLength = Math.Min(text.Length - cursor, maximumBytes + 1);
        var length = text.AsSpan(cursor, scanLength).IndexOf((byte)0);
        if (length < 0 || length > maximumBytes) throw new InvalidDataException();
        var start = cursor;
        cursor += length + 1;
        return (start, length);
    }

    internal readonly record struct FormatProfile(long FileLength, string Sha256, string HeaderHex,
        long TextOffset, int TextLength, int EntryCount);
    internal readonly record struct EntryAddress(string Word, int Offset, int Length);
    private sealed record LoadedState(byte[] Text, Dictionary<string, EntryAddress> Entries);
}
