using System.ComponentModel.DataAnnotations;

namespace Platform.Authorization;

/// <summary>
/// JWT signing settings shared by the platform for service-to-service tokens. Bound from the
/// "Jwt" configuration section. The signing key MUST be supplied via configuration (environment
/// profile or secret store); it is intentionally empty here so no static credential ships in code.
/// </summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "enterprise-auth-platform";
    public string Audience { get; set; } = "enterprise-services";

    [Required]
    public string SigningKey { get; set; } = string.Empty;
}
