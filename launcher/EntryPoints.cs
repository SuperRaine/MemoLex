using System;
using MemoLex.Packaging;

namespace MemoLex.Entry
{
    internal static class LauncherEntry { [STAThread] private static int Main(string[] args) { return Bootstrapper.Run(args, false); } }
    internal static class UninstallEntry { [STAThread] private static int Main(string[] args) { return Bootstrapper.Run(args, true); } }
}
