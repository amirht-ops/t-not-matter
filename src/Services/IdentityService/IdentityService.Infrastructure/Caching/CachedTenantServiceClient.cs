using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Errors;
using SharedKernel.Caching;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace IdentityService.Infrastructure.Caching;

public sealed class CachedTenantServiceClient(
    ITenantServiceClient inner,
    IDistributedCacheService cache) : ITenantServiceClient
{
    private static readonly CacheEntryOptions TenantSlugCacheOptions = CacheEntryOptions.DefaultTtl(TimeSpan.FromMinutes(5));
    private const string NotFoundSentinel = "__NOT_FOUND__";

    public async Task<Result<Guid>> ResolveTenantIdBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyBuilder.Combine("platform", "shared", "tenant", "slug", slug);

        var cached = await cache.GetAsync<SharedTenantCacheDto>(cacheKey, cancellationToken);
        if (cached is not null)
            return Result<Guid>.Success(cached.Id);

        var result = await inner.ResolveTenantIdBySlugAsync(slug, cancellationToken);

        if (result.IsSuccess)
        {
            await cache.SetAsync(cacheKey, new SharedTenantCacheDto(result.Value, slug), TenantSlugCacheOptions, cancellationToken);
        }

        return result;
    }

    public async Task<Result<bool>> ValidateDepartmentExistsAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        return await inner.ValidateDepartmentExistsAsync(tenantId, departmentId, cancellationToken);
    }
}
