namespace Platform.Caching.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";
    public int ConnectTimeoutMs { get; set; } = 5000;
    public int SyncTimeoutMs { get; set; } = 1000;
    public int RetryCount { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 200;
    public bool AbortOnConnectFail { get; set; } = false;
    public bool Ssl { get; set; } = false;
    public string? Password { get; set; }
    public int DatabaseId { get; set; } = -1;
}
