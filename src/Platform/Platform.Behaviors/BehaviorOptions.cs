namespace Platform.Behaviors;

public sealed class BehaviorOptions
{
    public bool EnableAuthorization { get; set; } = true;
    public bool EnableAudit { get; set; }
    public bool EnableUnitOfWork { get; set; } = true;
    public bool EnablePerformanceWarnings { get; set; } = true;
    public int PerformanceThresholdMs { get; set; } = 500;
    public bool EnableIdempotency { get; set; } = true;
    public bool EnableCaching { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableRetry { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public bool EnableFailClosed { get; set; } = true;
}
