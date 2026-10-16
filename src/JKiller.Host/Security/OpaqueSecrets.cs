using System.Security.Cryptography;
using System.Text;
namespace JKiller.Host.Security;

internal static class OpaqueSecrets
{
    public static string Create(char purpose, Guid id) => $"{purpose}.{id:N}.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    public static bool Matches(string secret, byte[] expected) => CryptographicOperations.FixedTimeEquals(Hash(secret), expected);
    public static bool TryId(string? token, char purpose, out Guid id)
    {
        id = default;
        return token is { Length: 78 } && token[0] == purpose && token[1] == '.' && token[34] == '.' &&
            token.AsSpan(35).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".AsSpan()) < 0 && Guid.TryParseExact(token.AsSpan(2, 32), "N", out id);
    }
}
