using System.Security.Cryptography;

namespace IdentityService.Domain.Aggregates.Session;

public sealed class RefreshToken
{
    private RefreshToken(string tokenHash, DateTimeOffset expiresAt)
    {
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public string TokenHash { get; }
    public DateTimeOffset ExpiresAt { get; }

    public const char Separator = '.';

    public static (RefreshToken RefreshToken, string PlainText) Create(Guid tenantId, Guid sessionId, DateTimeOffset expiresAt)
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var randomPart = Base64UrlEncode(bytes);
        var plain = $"{tenantId:N}{Separator}{sessionId:N}{Separator}{randomPart}";
        return (new RefreshToken(Hash(randomPart), expiresAt), plain);
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public static bool TryParse(string? token, out Guid tenantId, out Guid sessionId, out string randomPart)
    {
        tenantId = Guid.Empty;
        sessionId = Guid.Empty;
        randomPart = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split(Separator, 3);
        if (parts.Length != 3 ||
            !Guid.TryParse(parts[0], out var parsedTenant) ||
            parsedTenant == Guid.Empty ||
            !Guid.TryParse(parts[1], out var parsedSession) ||
            parsedSession == Guid.Empty ||
            string.IsNullOrWhiteSpace(parts[2]))
        {
            return false;
        }

        tenantId = parsedTenant;
        sessionId = parsedSession;
        randomPart = parts[2];
        return true;
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
