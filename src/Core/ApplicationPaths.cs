using System.IO;

namespace MemoLex.Core;

internal static class ApplicationPaths
{
    internal static string RuntimeDirectory=>Environment.ProcessPath is { } executable
        &&Path.GetFileName(executable).Equals("MemoLex.exe",StringComparison.OrdinalIgnoreCase)
        ?Path.GetDirectoryName(executable)!:AppContext.BaseDirectory;
    internal static string InstallDirectory=>FindInstallDirectory(RuntimeDirectory);
    internal static string Launcher=>Path.Combine(InstallDirectory,"MemoLex.exe");
    internal static string FindInstallDirectory(string runtimeDirectory)
    {
        var runtime=Path.GetFullPath(runtimeDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var parent=Path.GetDirectoryName(runtime);
        return Path.GetFileName(runtime).Equals("app",StringComparison.OrdinalIgnoreCase)&&parent!=null
            &&File.Exists(Path.Combine(runtime,"package.json"))&&File.Exists(Path.Combine(parent,"MemoLex.exe"))
            ?parent:runtime;
    }
}
