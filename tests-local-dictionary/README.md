# Local dictionary reader checks

The tests create synthetic fixtures at run time. Original dictionary files and
definitions are not distributed with the source repository.

Run the checks with a .NET 8 SDK:

```powershell
dotnet run --project tests-local-dictionary/MemoLex.LocalDictionaryTests.csproj --configuration Release
```

An optional read-only probe accepts a configured local file:

```powershell
dotnet run --project tests-local-dictionary/MemoLex.LocalDictionaryTests.csproj --configuration Release -- --live 'C:\Dictionaries\combined.bin'
```

The supported format is pinned to the inspected snapshot's full-file SHA-256,
header, record count and section bounds. A changed file requires independent
format inspection. Tests verify malformed data rejection, safe fallback and
preservation of the original file's bytes and modification time.

The local path is read from `MEMOLEX_LOCAL_DICTIONARY` or the private file
`%LOCALAPPDATA%/MemoLex/dictionary.path`. Without a configured local file, the
bundled dictionary remains available. No machine-specific path is embedded.
