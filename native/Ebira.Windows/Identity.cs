using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ebira.Core;

namespace Ebira.Windows;

public sealed class WindowsProtector : ISecretProtector
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public byte[] Protect(byte[] bytes) => Transform(bytes, true);
    public byte[] Unprotect(byte[] bytes) => Transform(bytes, false);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Blob output; var ok = encrypt ? CryptProtectData(ref input, "Ebira", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; } finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
    public static X509Certificate2 Certificate(string root)
    {
        Directory.CreateDirectory(root); var file = Path.Combine(root, "identity.bin"); var protector = new WindowsProtector();
        // Schannel needs a named Windows key container. The temporary current-user
        // key is removed when this certificate is disposed; the PFX stays DPAPI-protected.
        if (File.Exists(file)) return new X509Certificate2(protector.Unprotect(File.ReadAllBytes(file)), (string?)null, X509KeyStorageFlags.UserKeySet);
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=Ebira local transfer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usages = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }; request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
        var pfx = cert.Export(X509ContentType.Pfx); File.WriteAllBytes(file + ".tmp", protector.Protect(pfx)); File.Move(file + ".tmp", file);
        return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
    }
}
