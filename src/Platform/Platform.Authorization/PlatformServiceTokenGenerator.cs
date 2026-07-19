using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Principal;

namespace Platform.Authorization;

/// <summary>
/// Generates a short-lived service-to-service JWT at runtime using the platform's shared
/// signing key. The calling service identifies itself via <see cref="serviceName"/>; the
/// token is never read from static configuration.
/// </summary>
public sealed class PlatformServiceTokenGenerator(
    string serviceName,
    IOptions<JwtOptions> jwtOptions,
    IPlatformServiceRegistry serviceRegistry)
    : IServiceTokenGenerator
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromSeconds(90);

    public string GenerateServiceToken()
    {
        if (!serviceRegistry.IsKnownService(serviceName))
            throw new InvalidOperationException(
                $"Service '{serviceName}' is not registered in the platform service registry.");

        var options = jwtOptions.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Claims = new Dictionary<string, object>
            {
                ["principal_type"] = "service",
                ["service_name"] = serviceName,
                ["jti"] = Guid.NewGuid().ToString(),
                ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            },
            Expires = now.Add(TokenLifetime),
            SigningCredentials = credentials,
        });
    }
}
