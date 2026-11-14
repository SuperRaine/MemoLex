# EuDic brief-definition reader checks

`Program.cs` generates every fixture at run time. No EuDic dictionary bytes or
definitions are stored in this project, and the optional live probe prints only
validation results and an entry count.

Run with a .NET 8 SDK:

```powershell
dotnet run --project tests-eudic/MemoLex.EudicTests.csproj --configuration Release
```

The optional read-only probe accepts the user's original local file:

```powershell
dotnet run --project tests-eudic/MemoLex.EudicTests.csproj --configuration Release -- --live 'D:\oulu\eudic\dic\combined.bin'
```

The reader supports only the inspected 98,105,014-byte snapshot with SHA-256
`6dc57413125672017fa6fa3104a600934e38a1c51368ff3b3adc015cb7ad86de`.
It validates the complete file's identity, then reads the plaintext brief section
at byte offset 76,761,050 with length 13,359,209. That section contains 268,787
consecutive records; one has an empty brief definition. The adjacent Chinese
reverse index and the compressed main-definition section are not parsed.

Record layout is a UTF-8 null-terminated headword, an ignored 32-bit metadata
value, a 32-bit consecutive record ID, a UTF-8 null-terminated brief definition,
and an optional literal `<FORVO/>` marker. Maximum observed headword and brief
lengths are 50 and 455 bytes; the parser additionally enforces independent upper
bounds, strict UTF-8, unique case-insensitive headwords, sequential IDs, and exact
section consumption. The source file is opened with read access only.

This profile is intentionally version-specific. A different EuDic release needs
independent inspection; it must not be accepted by changing the hash alone.

On 2026-11-14 all six generated-fixture check groups and the optional live check
passed. The live file's SHA-256 and modification time were unchanged after load.

EuDic also documents an official application handoff using
[`eudic://dict/{word}`](https://docs.eudic.net/1/jin-jie-gong-neng/url-scheme).
It displays a lookup in EuDic and does not provide an embedded local-definition
response. Protocol registration was not present under the inspected machine's
`HKEY_CLASSES_ROOT\eudic` key, so installation support should be checked before
offering that handoff.
