using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoLex.Core;

var root = Path.Combine(Path.GetTempPath(), "MemoLex.LocalDictionaryTests." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
try
{
    void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    async Task Test(string name, Func<Task> action)
    {
        await action();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    static byte[] Section(params (string Word, string Definition, bool Marker)[] records)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, new UTF8Encoding(false), true);
        for (var id = 0; id < records.Length; id++)
        {
            var record = records[id];
            writer.Write(Encoding.UTF8.GetBytes(record.Word)); writer.Write((byte)0);
            writer.Write((uint)(100 + id)); writer.Write((uint)id);
            writer.Write(Encoding.UTF8.GetBytes(record.Definition)); writer.Write((byte)0);
            if (record.Marker) writer.Write("<FORVO/>"u8);
        }
        return bytes.ToArray();
    }

    (string Path, LocalBriefDictionary.FormatProfile Profile) Fixture(byte[] section, int count)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".bin");
        byte[] header = [0x4d, 0x4c, 0x58, 0x01];
        byte[] trailer = [0x01, 0x02, 0x03, 0x04];
        var file = header.Concat(section).Concat(trailer).ToArray();
        File.WriteAllBytes(path, file);
        return (path, new(file.Length, Convert.ToHexString(SHA256.HashData(file)),
            Convert.ToHexString(header), header.Length, section.Length, count));
    }

    async Task RejectedSection(byte[] section, int count)
    {
        var fixture = Fixture(section, count);
        var reader = new LocalBriefDictionary(fixture.Path, fixture.Profile);
        await reader.LoadAsync();
        Check(!reader.Available && reader.Count == 0 && reader.Lookup("synthetic") is null,
            "Malformed data was published");
        Check(!string.IsNullOrWhiteSpace(reader.LoadWarning), "Missing safe warning");
        Check(!reader.LoadWarning!.Contains(fixture.Path, StringComparison.OrdinalIgnoreCase),
            "Warning leaked a private path");
    }
    string BaseFixture()
    {
        var directory=Path.Combine(root,"base-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        using var data=File.Create(Path.Combine(directory,"dictionary.data"));
        using var index=new StreamWriter(Path.Combine(directory,"dictionary.index.tsv"),false,new UTF8Encoding(false));
        foreach(var word in new[]{"apple","__memolex_generated_only__"})
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new DictionaryEntry{word=word,translation="合成回退释义",phonetic="fixture-phonetic",exchange="s:apples",bnc="42"});
            index.WriteLine($"{word}\t{data.Position}\t{bytes.Length}");data.Write(bytes);
        }
        return directory;
    }

    await Test("Generated records support case-insensitive exact lookup and optional marker", async () =>
    {
        var fixture = Fixture(Section(("Synthetic", "合成示例\r\n第二行", true),
            ("synthetic phrase", "示例短语", false), ("vacant", "", false)), 3);
        var before = File.ReadAllBytes(fixture.Path);
        var reader = new LocalBriefDictionary(fixture.Path, fixture.Profile);
        Check(!reader.Available && reader.Count == 0 && reader.Lookup("Synthetic") is null,
            "Lookup available before load");
        await Task.WhenAll(reader.LoadAsync(), reader.LoadAsync());
        Check(reader.Available && reader.Count == 3 && reader.LoadWarning is null, "Valid fixture not loaded");
        Check(reader.TryLookup(" SYNTHETIC ", out var entry) && entry!.word == "Synthetic"
            && entry.translation == "合成示例\r\n第二行", "Exact lookup lost content/case");
        Check(entry!.definition == "" && entry.phonetic == "", "Unparsed fields were fabricated");
        Check(reader.Lookup("synthetic p") is null && reader.Lookup("") is null,
            "Prefix/empty lookup returned an entry");
        Check(reader.Lookup("vacant")?.translation == "", "An empty brief record was not preserved");
        Check(File.ReadAllBytes(fixture.Path).SequenceEqual(before), "Reader changed source bytes");
    });

    await Test("Wrong release and missing file fail safely", async () =>
    {
        var fixture = Fixture(Section(("synthetic", "示例", false)), 1);
        var wrongRelease = new LocalBriefDictionary(fixture.Path);
        await wrongRelease.LoadAsync();
        var missing = new LocalBriefDictionary(Path.Combine(root, "missing.bin"));
        await missing.LoadAsync();
        Check(!wrongRelease.Available && wrongRelease.Count == 0 && wrongRelease.LoadWarning is not null,
            "Unknown release was accepted");
        Check(!missing.Available && missing.LoadWarning is not null, "Missing file was not handled");
    });

    await Test("Truncation, changed header and changed outside bytes are rejected", async () =>
    {
        foreach (var kind in new[] { "truncate", "header", "tail" })
        {
            var fixture = Fixture(Section(("synthetic", "示例", false)), 1);
            var bytes = File.ReadAllBytes(fixture.Path);
            if (kind == "truncate") bytes = bytes[..^1];
            else bytes[kind == "header" ? 0 : bytes.Length - 1] ^= 0x10;
            File.WriteAllBytes(fixture.Path, bytes);
            var reader = new LocalBriefDictionary(fixture.Path, fixture.Profile);
            await reader.LoadAsync();
            Check(!reader.Available && reader.Count == 0 && reader.LoadWarning is not null,
                "Damaged file was accepted: " + kind);
        }
    });

    await Test("Malformed ordinal, duplicate word and trailing bytes reject the whole section", async () =>
    {
        var ordinal = Section(("synthetic", "示例", false));
        BinaryPrimitives.WriteUInt32LittleEndian(ordinal.AsSpan("synthetic".Length + 1 + 4, 4), 7);
        await RejectedSection(ordinal, 1);
        await RejectedSection(Section(("synthetic", "示例", false), ("SYNTHETIC", "重复", false)), 2);
        await RejectedSection(Section(("synthetic", "示例", false)).Concat(new byte[] { 9 }).ToArray(), 1);
    });

    await Test("Invalid UTF-8, missing terminators and excessive fields are rejected", async () =>
    {
        var invalid = Section(("synthetic", "示例", false));
        invalid[0] = 0xff;
        await RejectedSection(invalid, 1);
        await RejectedSection(Section(("synthetic", "示例", false))[..^1], 1);
        await RejectedSection(Section((new string('q', 257), "示例", false)), 1);
        await RejectedSection(Section(("synthetic", new string('x', 16_385), false)), 1);
        await RejectedSection(Section(("bad\nword", "示例", false)), 1);
        await RejectedSection(Section(("synthetic", "bad\u0001definition", false)), 1);
    });

    await Test("Out-of-range section cannot allocate or read beyond the file", async () =>
    {
        var fixture = Fixture(Section(("synthetic", "示例", false)), 1);
        foreach (var profile in new[]
        {
            fixture.Profile with { TextOffset = long.MaxValue },
            fixture.Profile with { TextLength = int.MaxValue },
            fixture.Profile with { EntryCount = int.MaxValue },
            fixture.Profile with { TextOffset = -1 }
        })
        {
            var reader = new LocalBriefDictionary(fixture.Path, profile);
            await reader.LoadAsync();
            Check(!reader.Available && reader.LoadWarning is not null, "Invalid bounds were accepted");
        }
    });
    await Test("A missing external dictionary keeps the bundled lookup and its metadata usable",async()=>
    {
        using var dictionary=new LocalDictionary(BaseFixture(),Path.Combine(root,"absent.bin"));await dictionary.LoadAsync();
        var entry=dictionary.Lookup("APPLE");
        Check(!dictionary.PreferredAvailable&&dictionary.Count==2&&entry?.translation=="合成回退释义"&&entry.phonetic=="fixture-phonetic","Missing external source broke the normal dictionary");
        Check(dictionary.LookupBySourceId(0)?.translation==entry!.translation,"Original source-index lookup changed");
    });

    if (args.Length == 2 && args[0] == "--live")
    {
        await Test("Optional live snapshot loads without changing its bytes", async () =>
        {
            var path = Path.GetFullPath(args[1]);
            static string Digest(string path)
            {
                using var stream = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            var before = Digest(path);
            var modified = File.GetLastWriteTimeUtc(path);
            var reader = new LocalBriefDictionary(path);
            await reader.LoadAsync();
            Check(reader.Available && reader.Count == 268_787 && reader.LoadWarning is null,
                "Verified snapshot did not load: " + reader.LoadWarning);
            foreach (var word in new[] { "apple", "hello", "dictionary", "test", "abandon", "probability" })
            {
                Check(reader.Lookup(word)?.translation.Length > 0, "Missing exact lookup: " + word);
                Check(reader.Lookup(word.ToUpperInvariant())?.translation == reader.Lookup(word)?.translation,
                    "Case-insensitive lookup failed: " + word);
            }
            Check(reader.Lookup("__memolex_missing_word__") is null, "Missing word produced a definition");
            Check(Digest(path) == before && File.GetLastWriteTimeUtc(path) == modified,
                "Source file identity or modification time changed");
            Console.WriteLine("LIVE brief entries: " + reader.Count + "; source bytes and modification time unchanged.");
        });
        await Test("The live external brief has priority while forms, phonetics and reverse-index meanings remain intact",async()=>
        {
            var local=new LocalBriefDictionary(args[1]);await local.LoadAsync();
            using var dictionary=new LocalDictionary(BaseFixture(),args[1]);await dictionary.LoadAsync();
            var entry=dictionary.Lookup("APPLE");
            Check(dictionary.PreferredAvailable&&dictionary.PreferredCount==268787&&entry?.source=="local-brief"&&entry.translation==local.Lookup("apple")?.translation,"External definition did not have priority");
            Check(entry!.phonetic=="fixture-phonetic"&&entry.exchange=="s:apples"&&entry.bnc=="42","Priority replacement erased ECDICT metadata");
            Check(dictionary.LookupBySourceId(0)?.translation=="合成回退释义","External priority changed the Chinese postings source meaning");
            Check(dictionary.Lookup("__memolex_generated_only__")?.translation=="合成回退释义","A missing external word did not fall back");
        });
    }
    else if (args.Length != 0) throw new ArgumentException("Usage: [--live <combined.bin path>]");

    Console.WriteLine($"Passed {passed} local dictionary reader checks.");
}
finally
{
    Directory.Delete(root, true);
}
