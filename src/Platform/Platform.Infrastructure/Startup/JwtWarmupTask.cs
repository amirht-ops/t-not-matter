using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class JwtWarmupTask : IStartupTask
{
    private readonly ILogger<JwtWarmupTask> _logger;
    private readonly JwtWarmupOptions _options;

    public JwtWarmupTask(
        ILogger<JwtWarmupTask> logger,
        JwtWarmupOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public string DisplayName => "JWT Warmup";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Warming up JWT handler");

        try
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_options.SigningKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var handler = new JsonWebTokenHandler();
            var now = DateTime.UtcNow;

            var token = handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = "warmup",
                    ["jti"] = Guid.NewGuid().ToString(),
                    ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
                },
                Expires = now.AddMinutes(1),
                SigningCredentials = credentials,
            });

            var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            });

            _logger.LogInformation("JWT handler warmup completed (token validated: {IsValid})", result.IsValid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT warmup failed");
        }
    }
}

public sealed class JwtWarmupOptions
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "enterprise-auth-platform";
    public string Audience { get; set; } = "enterprise-services";
}
