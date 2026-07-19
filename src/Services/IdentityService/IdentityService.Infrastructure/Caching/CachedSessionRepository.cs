using IdentityService.Domain.Aggregates.Session;
using IdentityService.Domain.Repositories;
using SharedKernel.Caching;

namespace IdentityService.Infrastructure.Caching;

public sealed class CachedSessionRepository(ISessionRepository inner, IDistributedCacheService cache) : ISessionRepository
{
    private static readonly CacheEntryOptions PositiveCacheOptions = new() { SlidingExpiration = TimeSpan.FromSeconds(30) };
    private static readonly CacheEntryOptions NegativeCacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(3) };

    public async Task<Session?> GetByIdAsync(Guid tenantId, Guid sessionId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByIdAsync(tenantId, sessionId, trackChanges, cancellationToken);

        var key = SessionByIdKey(tenantId, sessionId);
        var cached = await cache.GetAsync<Session>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var session = await inner.GetByIdAsync(tenantId, sessionId, trackChanges, cancellationToken);
        await cache.SetAsync(key, session, session is null ? NegativeCacheOptions : PositiveCacheOptions, cancellationToken);
        return session;
    }

    public Task<Session?> GetByRefreshTokenAsync(string refreshToken, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        inner.GetByRefreshTokenAsync(refreshToken, trackChanges, cancellationToken);

    public Task<Session?> GetByPreviousRefreshTokenAsync(string refreshToken, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        inner.GetByPreviousRefreshTokenAsync(refreshToken, trackChanges, cancellationToken);

    public Task<IReadOnlyList<Session>> GetByUserIdAsync(Guid tenantId, Guid userId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        inner.GetByUserIdAsync(tenantId, userId, trackChanges, cancellationToken);

    public Task<IReadOnlyList<Session>> GetByTenantIdAsync(Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        inner.GetByTenantIdAsync(tenantId, trackChanges, cancellationToken);

    public async Task AddAsync(Session session, CancellationToken cancellationToken)
    {
        await inner.AddAsync(session, cancellationToken);
    }

    public async Task UpdateAsync(Session session, CancellationToken cancellationToken)
    {
        await inner.UpdateAsync(session, cancellationToken);
        await cache.RemoveAsync(SessionByIdKey(session.TenantId, session.Id), cancellationToken);
    }

    private static string SessionByIdKey(Guid tenantId, Guid sessionId) => $"identity:session:id:{tenantId:N}:{sessionId:N}";
}