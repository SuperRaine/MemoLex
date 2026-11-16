using System;
using System.IO;
using MemoLex.Packaging;

internal static class MaintenanceEntry
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--uninstall-cleanup")
            {
                var keep = args[2] == "1";
                var root = Path.GetFullPath(args[1]);
                UninstallOperations.CloseApplications(root, !keep);
                if (!keep) UninstallOperations.RemoveProfile(Paths.UserData);
                UninstallOperations.CleanRegistration(root, !keep);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--close-running") { UninstallOperations.CloseApplications(Path.GetFullPath(args[1]), false); return 0; }
            throw new ArgumentException("卸载参数无效。");
        }
        catch (Exception error)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "MemoLex-maintenance.log"), DateTime.UtcNow.ToString("O") + " " + error.GetType().Name + " " + error.HResult + Environment.NewLine); } catch { }
            return 1;
        }
    }
}
