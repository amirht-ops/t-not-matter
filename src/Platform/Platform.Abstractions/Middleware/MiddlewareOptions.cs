namespace Platform.Abstractions.Middleware;

public sealed class MiddlewareOptions
{
    public const string SectionName = "Middleware";

    public bool EnableExceptionHandling { get; set; } = true;
    public bool EnableTenantResolution { get; set; } = true;
    public bool EnableCorrelation { get; set; } = true;
    public bool EnableRequestLogging { get; set; } = true;
    public bool EnableRateLimiting { get; set; } = false;
}
