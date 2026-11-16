using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace MemoLex.Packaging
{
    public sealed class RemovalResult
    {
        public int RemovedFiles { get; set; }
        public int PreservedFiles { get; set; }
    }
    public static class UninstallOperations
    {
        public const string InstallerKey = @"Software\SuperRaine\MemoLex\Installer";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MemoLex";
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        public static void CloseApplications(string root, bool allCopies)
        {
            foreach (var process in Process.GetProcessesByName("MemoLex"))
            {
                using (process)
                {
                    string path; DateTime started;
                    try
                    {
                        path = process.MainModule.FileName; started = process.StartTime;
                        if (process.Id == Process.GetCurrentProcess().Id || process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                        if (!Paths.Within(path, root) && (!allCopies || FileVersionInfo.GetVersionInfo(path).ProductName != "墨墨搜词 MemoLex")) continue;
                    }
                    catch { continue; }
                    PostMessage(process.MainWindowHandle, RegisterWindowMessage("MemoLex.Maintenance.Exit.v1"), IntPtr.Zero, IntPtr.Zero);
                    if (process.WaitForExit(3000)) continue;
                    try
                    {
                        if (!process.HasExited && process.StartTime == started && string.Equals(process.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase))
                        { process.Kill(); process.WaitForExit(5000); }
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
        public static string MsiProduct(string root)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(InstallerKey))
            {
                var location = key == null ? null : key.GetValue("InstallDirectory") as string;
                var product = key == null ? null : key.GetValue("ProductCode") as string;
                Guid parsed;
                return location != null && Paths.Same(location, root) && Guid.TryParse(product, out parsed) ? parsed.ToString("B") : null;
            }
        }
        public static void RemoveProfile(string dataRoot)
        {
            // The caller supplies only the current user's fixed MemoLex data directory.
            // Build a list without following links before removing any data.
            if (!Directory.Exists(dataRoot)) return;
            if (Paths.Same(dataRoot, Path.GetPathRoot(Path.GetFullPath(dataRoot)))) throw new IOException("不能将磁盘根目录当作用户资料目录。");
            var directories = new List<string>(); var files = new List<string>();
            Action<string> collect = null;
            collect = directory =>
            {
                Paths.CheckLinks(directory, dataRoot); directories.Add(directory);
                foreach (var file in Directory.GetFiles(directory)) { Paths.CheckLinks(file, dataRoot); files.Add(file); }
                foreach (var child in Directory.GetDirectories(directory)) collect(child);
            };
            collect(dataRoot);
            foreach (var file in files)
            {
                Paths.CheckLinks(file, dataRoot);
                for (var attempt = 0; ; attempt++)
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); break; }
                    catch (IOException) { if (attempt >= 9) throw; Thread.Sleep(200); Paths.CheckLinks(file, dataRoot); }
                }
            }
            foreach (var directory in directories.OrderByDescending(p => p.Length))
            { Paths.CheckLinks(directory, dataRoot); Directory.Delete(directory, false); }
        }
        public static RemovalResult RemovePackage(string root, PackageManifest manifest)
        {
            var result = new RemovalResult(); var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest.Files)
            {
                var file = Paths.Child(root, item.Path);
                if (!File.Exists(file)) continue;
                if (!PackageManifest.Hash(file).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) { result.PreservedFiles++; continue; }
                File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); result.RemovedFiles++;
                var parent = Path.GetDirectoryName(file);
                while (!Paths.Same(parent, root) && Paths.Within(parent, root)) { directories.Add(parent); parent = Path.GetDirectoryName(parent); }
            }
            var description = Paths.Child(root, "app/package.json"); if (File.Exists(description)) File.Delete(description);
            directories.Add(Path.Combine(root, "app"));
            foreach (var directory in directories.OrderByDescending(p => p.Length))
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) { Paths.CheckLinks(directory, root); Directory.Delete(directory, false); }
            // Never recursively delete an application folder, even on a drive root.
            if (!Paths.Same(root, Path.GetPathRoot(Path.GetFullPath(root))) && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root, false);
            return result;
        }
        public static void CleanRegistration(string root, bool removeData)
        {
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                var command = run == null ? null : run.GetValue("MemoLex") as string;
                if (command != null && (command.IndexOf(Paths.Full(root), StringComparison.OrdinalIgnoreCase) >= 0 || removeData && command.IndexOf("MemoLex.exe", StringComparison.OrdinalIgnoreCase) >= 0)) run.DeleteValue("MemoLex", false);
            }
            foreach (var location in new[] { InstallerKey, UninstallKey })
            {
                using (var key = Registry.CurrentUser.OpenSubKey(location))
                {
                    var path = key == null ? null : key.GetValue(location == InstallerKey ? "InstallDirectory" : "InstallLocation") as string;
                    if (path == null || !Paths.Same(path, root)) continue;
                }
                Registry.CurrentUser.DeleteSubKeyTree(location, false);
            }
        }
        public static RemovalResult Remove(string root, PackageManifest manifest, bool keepData, Action<string> status)
        {
            status("正在关闭墨墨搜词…"); CloseApplications(root, !keepData);
            var product = MsiProduct(root);
            if (product != null)
            {
                status("正在卸载软件…");
                var log = Path.Combine(Path.GetTempPath(), "MemoLex-uninstall-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"), "/x " + product + " /qn /norestart MEMOLEX_KEEP_USER_DATA=" + (keepData ? "1" : "0") + " /L*v " + Paths.Quote(log))
                { UseShellExecute = false, CreateNoWindow = true };
                using (var installer = Process.Start(start))
                { installer.WaitForExit(); if (installer.ExitCode != 0 && installer.ExitCode != 3010 && installer.ExitCode != 1605) throw new IOException("卸载未完成（错误 " + installer.ExitCode + "），日志已保存到临时文件夹。"); }
            }
            status("正在移除软件文件…"); var result = RemovePackage(root, manifest);
            if (!keepData) { status("正在删除用户资料…"); RemoveProfile(Paths.UserData); }
            CleanRegistration(root, !keepData); return result;
        }
    }
}
