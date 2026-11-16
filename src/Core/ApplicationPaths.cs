using System.IO;

namespace MemoLex.Core;

internal static class ApplicationPaths
{
    internal static string RuntimeDirectory=>Environment.ProcessPath is { } executable
        &&Path.GetFileName(executable).Equals("MemoLex.exe",StringComparison.OrdinalIgnoreCase)
        ?Path.GetDirectoryName(executable)!:AppContext.BaseDirectory;
    internal static string InstallDirectory=>FindInstallDirectory(RuntimeDirectory);
    internal static string Launcher=>Path.Combine(InstallDirectory,"MemoLex.exe");
    internal static string NormalizeDirectory(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    internal static bool SameDirectory(string left,string right)=>NormalizeDirectory(left).Equals(NormalizeDirectory(right),StringComparison.OrdinalIgnoreCase);
    internal static bool IsVolumeRoot(string path)=>SameDirectory(path,Path.GetPathRoot(Path.GetFullPath(path))!);
    internal static string UpdateDirectory(string directory)=>IsVolumeRoot(directory)?Path.Combine(NormalizeDirectory(directory),"MemoLex"):NormalizeDirectory(directory);
    internal static string FindInstallDirectory(string runtimeDirectory)
    {
        var runtime=NormalizeDirectory(runtimeDirectory);
        var parent=Path.GetDirectoryName(runtime);
        return Path.GetFileName(runtime).Equals("app",StringComparison.OrdinalIgnoreCase)&&parent!=null
            &&File.Exists(Path.Combine(runtime,"package.json"))&&File.Exists(Path.Combine(parent,"MemoLex.exe"))
            ?parent:runtime;
    }
}
