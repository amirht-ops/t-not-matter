namespace Platform.Authorization;

public sealed class AuthorizationServiceClientOptions
{
    public const string SectionName = "Services:AuthorizationService";

    public string BaseUrl { get; set; } = "http://localhost:5001";
    public int TimeoutSeconds { get; set; } = 5;
    public int RetryCount { get; set; } = 3;
    public int CircuitBreakerThreshold { get; set; } = 3;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
}
