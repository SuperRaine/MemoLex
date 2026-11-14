using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MemoLex.Services;

/// <summary>Windows user-bound DPAPI credentials. No token is written as plaintext.</summary>
public sealed partial class TokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MemoLex.Token.v1");
    public static string TokenPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoLex", "token.dpapi");
    private readonly string _tokenPath;

    public TokenStore() : this(TokenPath) { }
    internal TokenStore(string tokenPath) => _tokenPath = tokenPath;

    public bool HasToken => GetToken() is { Length: > 0 };
    internal string? CredentialKey=>Fingerprint(GetToken());
    internal static string? Fingerprint(string? token)
    {
        if(string.IsNullOrEmpty(token))return null;
        var bytes=Encoding.UTF8.GetBytes(token);
        try{return Convert.ToHexString(SHA256.HashData(bytes));}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }

    public string? GetToken()=>ReadActiveToken();

    public void SaveToken(string token)
    {
        SaveAccountToken(token,null,null,null,null);
    }

    public void DeleteToken()
    {
        if (File.Exists(_tokenPath)) File.Delete(_tokenPath);
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBlob = Allocate(input);
        var entropyBlob = Allocate(Entropy);
        var result = new DataBlob();
        try
        {
            const int CryptprotectUiForbidden = 1;
            var success = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out result)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out result);
            if (!success) throw new CryptographicException("Windows 无法读取或保存此账户的加密凭据。");
            var bytes = new byte[result.Length];
            Marshal.Copy(result.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            ClearAndFree(inputBlob);
            ClearAndFree(entropyBlob);
            if (result.Data != IntPtr.Zero)
            {
                for (var index = 0; index < result.Length; index++) Marshal.WriteByte(result.Data, index, 0);
                LocalFree(result.Data);
            }
        }
    }

    private static DataBlob Allocate(byte[] value)
    {
        var pointer = Marshal.AllocHGlobal(value.Length);
        Marshal.Copy(value, 0, pointer, value.Length);
        return new DataBlob { Length = value.Length, Data = pointer };
    }

    private static void ClearAndFree(DataBlob value)
    {
        if (value.Data == IntPtr.Zero) return;
        for (var index = 0; index < value.Length; index++) Marshal.WriteByte(value.Data, index, 0);
        Marshal.FreeHGlobal(value.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
