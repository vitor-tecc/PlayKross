using JKiller.Contracts;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
namespace JKiller.Windows.Security;
public sealed record SavedSession(Uri Origin, SessionGrant Grant);
public sealed class SessionVault(string directory, TimeProvider? clock = null)
{
    private readonly string file = Path.Combine(Path.GetFullPath(directory), "session.dpapi");
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public async Task SaveAsync(SavedSession session, CancellationToken token)
    {
        if (session.Grant.ExpiresAt <= time.GetUtcNow() || string.IsNullOrEmpty(session.Grant.SessionToken)) throw new ArgumentException("An active room session is required.");
        var info = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user identity unavailable.");
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
        var plain = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            if (plain.Length > 65536) throw new ArgumentException("Room session exceeds the storage limit.");
            var encrypted = Transform(plain, true);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temporary, encrypted, token); File.Move(temporary, file, true); }
            finally { File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public async Task<SavedSession?> RestoreAsync(Uri origin, CancellationToken token)
    {
        if (!File.Exists(file) || new FileInfo(file).Length > 131072) return null;
        try
        {
            var plain = Transform(await File.ReadAllBytesAsync(file, token), false);
            try
            {
                var session = JsonSerializer.Deserialize<SavedSession>(plain);
                return session?.Origin == origin && session.Grant.ExpiresAt > time.GetUtcNow() && !string.IsNullOrEmpty(session.Grant.SessionToken) ? session : null;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (error is CryptographicException or JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = protect ? CryptProtectData(ref input, null, 0, 0, 0, 1, out output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!success) throw new CryptographicException("Windows could not protect or restore this room session.");
            if (output.Length is < 0 or > 131072) throw new CryptographicException("Protected session exceeds the storage limit.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0) { for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
