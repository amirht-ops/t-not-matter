using SharedKernel.Caching;
using SharedKernel.Domain.ValueObjects;
using TenantService.Domain.Aggregates;
using TenantService.Domain.Repositories;
using TenantCode = TenantService.Domain.ValueObjects.TenantCode;

namespace TenantService.Infrastructure.Caching;

public sealed class CachedTenantRepository(
    ITenantRepository inner,
    IDistributedCacheService cache) : ITenantRepository
{
    private static readonly CacheEntryOptions TenantCacheOptions = new() { SlidingExpiration = TimeSpan.FromSeconds(30) };

    public async Task<Tenant?> GetByIdAsync(Guid id, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByIdAsync(id, trackChanges, cancellationToken);

        var key = MetadataKey(id);
        return await cache.GetOrCreateAsync(key, ct => inner.GetByIdAsync(id, trackChanges, ct), TenantCacheOptions, cancellationToken);
    }

    public async Task<Tenant?> GetByCodeAsync(TenantCode code, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByCodeAsync(code, trackChanges, cancellationToken);

        var key = IdentifierKey(code.Value);
        return await cache.GetOrCreateAsync(key, ct => inner.GetByCodeAsync(code, trackChanges, ct), TenantCacheOptions, cancellationToken);
    }

    public async Task<Tenant?> GetBySlugAsync(TenantSlug slug, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetBySlugAsync(slug, trackChanges, cancellationToken);

        var slugValue = slug.Value;
        var key = SharedSlugKey(slugValue);

        var dto = await cache.GetAsync<SharedTenantCacheDto>(key, cancellationToken);
        if (dto is not null)
            return await inner.GetByIdAsync(dto.Id, cancellationToken: cancellationToken);

        var tenant = await inner.GetBySlugAsync(slug, cancellationToken: cancellationToken);
        if (tenant is not null)
            await cache.SetAsync(key, new SharedTenantCacheDto(tenant.Id, slugValue), TenantCacheOptions, cancellationToken);

        return tenant;
    }

    public async Task<bool> ExistsByCodeAsync(TenantCode code, CancellationToken cancellationToken)
    {
        var key = IdentifierKey(code.Value);
        var cached = await cache.GetAsync<Tenant>(key, cancellationToken);
        if (cached is not null) return true;

        return await inner.ExistsByCodeAsync(code, cancellationToken);
    }

    public async Task<bool> ExistsBySlugAsync(TenantSlug slug, CancellationToken cancellationToken)
    {
        var key = SharedSlugKey(slug.Value);
        var cached = await cache.GetAsync<SharedTenantCacheDto>(key, cancellationToken);
        if (cached is not null) return true;

        return await inner.ExistsBySlugAsync(slug, cancellationToken);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await inner.AddAsync(tenant, cancellationToken);
        await InvalidateCacheAsync(tenant, cancellationToken);
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await inner.UpdateAsync(tenant, cancellationToken);
        await InvalidateCacheAsync(tenant, cancellationToken);
    }

    private async Task InvalidateCacheAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(MetadataKey(tenant.Id), cancellationToken);
        await cache.RemoveAsync(IdentifierKey(tenant.Identifier), cancellationToken);
        await cache.RemoveAsync(SharedSlugKey(tenant.Slug.Value), cancellationToken);
    }

    private static string MetadataKey(Guid id) => CacheKeyBuilder.Combine("tenant-service", "tenant", "id", id.ToString("N"));
    private static string IdentifierKey(string identifier) => CacheKeyBuilder.Combine("tenant-service", "tenant", "code", identifier);
    private static string SharedSlugKey(string slug) => CacheKeyBuilder.Combine("platform", "shared", "tenant", "slug", slug);
}
