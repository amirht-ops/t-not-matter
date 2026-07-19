using Microsoft.Extensions.Caching.Memory;

namespace IdentityService.Infrastructure.Caching;

public sealed class SessionCache(IMemoryCache cache)
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(5);

    public void Set(Guid tenantId, Guid sessionId, DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        if (expiresAt <= now)
        {
            Remove(tenantId, sessionId);
            return;
        }

        cache.Set(
            Key(tenantId, sessionId),
            expiresAt,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = expiresAt,
                SlidingExpiration = SlidingExpiration
            });
    }

    public bool IsActive(Guid tenantId, Guid sessionId) =>
        cache.TryGetValue(Key(tenantId, sessionId), out DateTimeOffset expiresAt) &&
        expiresAt > DateTimeOffset.UtcNow;

    public void Remove(Guid tenantId, Guid sessionId) => cache.Remove(Key(tenantId, sessionId));

    private static string Key(Guid tenantId, Guid sessionId) => $"identity:session:v1:{tenantId}:{sessionId}";
}
