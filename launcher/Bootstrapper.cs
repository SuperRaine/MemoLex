using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MemoLex.Packaging
{
    public static class Bootstrapper
    {
        [STAThread]
        public static int Run(string[] args, bool uninstall)
        {
            try
            {
                var ownRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (uninstall)
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    if (args.Length == 2 && args[0] == "--helper")
                    {
                        var root = Path.GetFullPath(args[1]); var manifest = PackageManifest.Read(root);
                        if (!PackageManifest.Hash(Path.Combine(root, "MemoLex.dll")).Equals(PackageManifest.Hash(Assembly.GetExecutingAssembly().Location), StringComparison.OrdinalIgnoreCase)) throw new IOException("软件已更新，请重新打开卸载程序。");
                        Application.Run(new UninstallWindow(root, manifest)); return 0;
                    }
                    PackageManifest.Read(ownRoot);
                    var temporary = Path.Combine(Path.GetTempPath(), "MemoLex.Uninstall." + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
                    File.Copy(Path.Combine(ownRoot, "Uninstall.exe"), Path.Combine(temporary, "Uninstall.exe"));
                    File.Copy(Path.Combine(ownRoot, "MemoLex.dll"), Path.Combine(temporary, "MemoLex.dll"));
                    Process.Start(new ProcessStartInfo(Path.Combine(temporary, "Uninstall.exe"), "--helper " + Paths.Quote(ownRoot)) { UseShellExecute = false, WorkingDirectory = temporary }); return 0;
                }
                var executable = Path.Combine(ownRoot, "app", "MemoLex.exe");
                if (!File.Exists(executable)) throw new IOException("运行文件缺失，请重新解压完整软件包，保留 app 文件夹。");
                var start = new ProcessStartInfo(executable, string.Join(" ", args.Select(Paths.Quote))) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
                start.EnvironmentVariables["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(Paths.UserData, "runtime");
                using (var process = Process.Start(start))
                {
                    if (args.Contains("--smoke-test")) { process.WaitForExit(); return process.ExitCode; }
                    return 0;
                }
            }
            catch (Exception error)
            { MessageBox.Show(error is IOException || error is UnauthorizedAccessException ? error.Message : "无法打开墨墨搜词，请重新解压完整软件包。", "墨墨搜词", MessageBoxButtons.OK, MessageBoxIcon.Information); return 1; }
        }
    }
}
