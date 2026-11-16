using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace MemoLex.Packaging
{
    [DataContract]
    public sealed class PackageFile
    {
        [DataMember] public string Path { get; set; }
        [DataMember] public string Sha256 { get; set; }
    }
    [DataContract]
    public sealed class PackageManifest
    {
        public const string Identity = "MemoLex.Desktop.9A25DBAA-234C-48EA-96DC-1DE36D9872F7";
        [DataMember] public string ProductId { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public int Schema { get; set; }
        [DataMember] public PackageFile[] Files { get; set; }
        public static PackageManifest Read(string root)
        {
            var path = Paths.Child(root, "app/package.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException("找不到完整的软件清单，请重新解压软件包后重试。");
            PackageManifest manifest;
            using (var stream = File.OpenRead(path)) manifest = (PackageManifest)new DataContractJsonSerializer(typeof(PackageManifest)).ReadObject(stream);
            if (manifest == null || manifest.Schema != 1 || manifest.ProductId != Identity || manifest.Files == null || manifest.Files.Length < 4 || manifest.Files.Length > 20000)
                throw new IOException("软件文件清单无效，请重新解压完整软件包。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                Paths.Child(root, file.Path);
                if (file.Path.Equals("app/package.json", StringComparison.OrdinalIgnoreCase) || !names.Add(file.Path) || file.Sha256 == null || file.Sha256.Length != 64 || file.Sha256.Any(c => !Uri.IsHexDigit(c)))
                    throw new IOException("软件文件清单包含无效内容。");
            }
            if (!new[] { "MemoLex.exe", "MemoLex.dll", "Uninstall.exe", "app/MemoLex.exe" }.All(names.Contains))
                throw new IOException("软件文件清单缺少必要入口。");
            return manifest;
        }
        public static string Hash(string path)
        {
            using (var input = File.OpenRead(path)) using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
    public static class Paths
    {
        public static string Full(string path) { return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar; }
        public static bool Same(string left, string right) { return Full(left).Equals(Full(right), StringComparison.OrdinalIgnoreCase); }
        public static bool Within(string path, string root) { return Full(path).StartsWith(Full(root), StringComparison.OrdinalIgnoreCase); }
        public static string Child(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0
                || relative.Replace('\\', '/').Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ")))
                throw new IOException("软件清单中的路径无效。");
            var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
            if (!target.StartsWith(Full(root), StringComparison.OrdinalIgnoreCase)) throw new IOException("软件清单中的路径超出了软件目录。");
            CheckLinks(target, root); return target;
        }
        public static void CheckLinks(string path, string root)
        {
            var current = System.IO.Path.GetFullPath(path);
            while (Within(current, root))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("目录中存在文件链接，未自动处理：" + current);
                if (Same(current, root)) break;
                current = System.IO.Path.GetDirectoryName(current);
            }
        }
        public static string UserData { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoLex"); } }
        public static string Quote(string value)
        {
            var result = new System.Text.StringBuilder("\""); var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
                else result.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
