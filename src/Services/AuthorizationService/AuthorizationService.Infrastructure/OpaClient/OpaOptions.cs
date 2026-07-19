using System.ComponentModel.DataAnnotations;

namespace AuthorizationService.Infrastructure.OpaClient;

public sealed class OpaOptions
{
    [Required] public string BaseUrl { get; init; } = "http://localhost:8181";
    [Required] public string PolicyPath { get; init; } = "/v1/data/authorization/allow";
    public int TimeoutMs { get; init; } = 1000;
    public int CircuitBreakerThreshold { get; init; } = 5;
    public int CircuitBreakerDurationSeconds { get; init; } = 30;
    public int RetryCount { get; init; } = 2;
}
