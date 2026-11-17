using System.Security.Cryptography;
using System.Text;

namespace MemoLex.Core
{
    internal static class ApplicationPaths
    {
        internal static string RuntimeDirectory => Environment.GetEnvironmentVariable("MEMOLEX_RESOURCES") ?? AppContext.BaseDirectory;
    }
}

namespace MemoLex.Services
{
    // Credentials are held in the macOS Keychain by the native app. The worker
    // receives only the currently selected credential through its private pipe.
    public sealed class TokenStore
    {
        private string? _token;
        public string? GetToken() => Volatile.Read(ref _token);
        public bool HasToken => GetToken() is { Length: > 0 };
        internal string? CredentialKey => Fingerprint(GetToken());
        internal void Set(string? token)
        {
            if (token != null && (token.Length is < 16 or > 2048 || token.Any(char.IsControl)))
                throw new ArgumentException("无效的登录信息，请重新登录。");
            Volatile.Write(ref _token, token);
        }
        internal static string? Fingerprint(string? token) => string.IsNullOrEmpty(token) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
