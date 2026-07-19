using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Application;
using SharedKernel.Caching;
using SharedKernel.Results;

namespace TenantService.Application.Features.GetTenantBySlug;

public sealed record GetTenantBySlugQuery(string Slug, Guid CorrelationId) : IRequest<Result<TenantSlugDto>>, ICachedQuery, IResolveTenantInternally
{
    public string CacheKey => CacheKeyBuilder.Combine("tenant-service", "by-slug", Slug.ToLowerInvariant());
    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(5);
}

public sealed record TenantSlugDto(Guid TenantId, string Name, string Slug);
