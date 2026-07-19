namespace Platform.Caching.Configuration;

public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    public bool Enabled { get; set; } = true;
    public int DefaultTtlSeconds { get; set; } = 60;
    public bool SerializeAsJson { get; set; } = true;
    public string KeyPrefix { get; set; } = "esp";
}
