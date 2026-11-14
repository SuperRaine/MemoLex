namespace MemoLex.Services;

internal static class BundledTranslationKey
{
    private static readonly Lazy<string?> Value=new(()=>
    {
        using var stream=typeof(BundledTranslationKey).Assembly.GetManifestResourceStream("MemoLex.DeepL.key");
        if(stream==null||stream.Length>256)return null;
        using var reader=new System.IO.StreamReader(stream);
        var key=reader.ReadToEnd().Trim();return key.Length>=16?key:null;
    });
    internal static string? Get()=>Value.Value;
}
