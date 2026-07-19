namespace Platform.Infrastructure.Startup;

public sealed class StartupTaskOptions
{
    public Dictionary<string, StartupTaskPolicy> Tasks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StartupTaskPolicy
{
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 0;
    public int RetryDelayBaseMs { get; set; } = 1000;
    public Func<Exception, bool> IsTransient { get; set; } = ExceptionClassifier.IsTransient;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public TimeSpan GetRetryDelay(int attempt)
    {
        var delayMs = RetryDelayBaseMs * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, 30_000));
    }
}

public static class StartupTaskDefaults
{
    public const int DefaultTimeoutSeconds = 30;
    public const int DefaultMaxRetries = 0;
    public const int DefaultRetryDelayBaseMs = 1000;

    public static readonly Dictionary<string, StartupTaskPolicy> DefaultPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Database"] = new() { TimeoutSeconds = 30, MaxRetries = 3, RetryDelayBaseMs = 1000 },
        ["Redis"] = new() { TimeoutSeconds = 10, MaxRetries = 3, RetryDelayBaseMs = 500 },
        ["HttpClient Warmup"] = new() { TimeoutSeconds = 15, MaxRetries = 0, RetryDelayBaseMs = 1000 },
        ["JWT Warmup"] = new() { TimeoutSeconds = 5, MaxRetries = 0, RetryDelayBaseMs = 1000 },
    };
}
