namespace SharedKernel.Caching;

public sealed record CacheEntryOptions
{
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }
    public bool FailSafe { get; init; } = true;

    public static CacheEntryOptions DefaultTtl(TimeSpan ttl) => new()
    {
        AbsoluteExpirationRelativeToNow = ttl,
        FailSafe = true
    };

    public static readonly CacheEntryOptions FiveSeconds = DefaultTtl(TimeSpan.FromSeconds(5));
    public static readonly CacheEntryOptions OneMinute = DefaultTtl(TimeSpan.FromMinutes(1));
    public static readonly CacheEntryOptions FiveMinutes = DefaultTtl(TimeSpan.FromMinutes(5));
}
