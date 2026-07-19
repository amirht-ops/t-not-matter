using System.Security.Cryptography;
using System.Text;
using IdentityService.Domain.Services;

namespace IdentityService.Infrastructure.Mfa;

public sealed class TotpMfaProvider : IMfaProvider
{
    public bool VerifyCode(string secret, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        return Enumerable.Range(-1, 3).Any(offset => Generate(secret, now.AddSeconds(offset * 30)) == code.Trim());
    }

    private static string Generate(string secret, DateTimeOffset now)
    {
        var timestep = now.ToUnixTimeSeconds() / 30;
        var key = FromBase32(secret);
        var counter = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(timestep));
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = input.Trim().ToUpperInvariant().Replace("=", "");
        var bits = 0;
        var value = 0;
        var output = new List<byte>(cleaned.Length * 5 / 8 + 1);
        foreach (var c in cleaned)
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
            {
                continue;
            }

            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
