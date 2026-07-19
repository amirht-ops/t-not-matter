namespace Platform.Abstractions.Middleware;

public sealed class RateLimitingOptions
{
    public const string SectionName = "Middleware:RateLimiting";

    public string DefaultPolicyName { get; set; } = "default";
    public int DefaultPermitLimit { get; set; } = 100;
    public int DefaultWindowSeconds { get; set; } = 60;
    public bool UseClientIpAsKey { get; set; } = true;
    public bool UseUserIdAsKey { get; set; }
    public int RetryAfterSeconds { get; set; } = 60;
}
