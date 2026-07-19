using System.ComponentModel.DataAnnotations;

namespace IdentityService.Infrastructure.Options;

public sealed class AuthorizationRoleResolverOptions
{
    public const string SectionName = "Services:AuthorizationService";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 10;

    [Range(0, 20)]
    public int RetryCount { get; set; } = 3;

    [Range(1, 100)]
    public int CircuitBreakerThreshold { get; set; } = 3;

    [Range(1, 3600)]
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
}
