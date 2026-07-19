using SharedKernel.Authorization;
using SharedKernel.Caching;

namespace IdentityService.Infrastructure.Caching;

public sealed class CachedAuthorizationDecisionService(
    IAuthorizationDecisionService inner,
    IDistributedCacheService cache) : IAuthorizationDecisionService
{
    private static readonly CacheEntryOptions CacheOptions = CacheEntryOptions.DefaultTtl(TimeSpan.FromMinutes(10));

    public async Task<AuthorizationDecision> DecideAsync(AuthorizationRequest request, CancellationToken ct)
    {
        if (!IsPlatformAdminCheck(request))
            return await inner.DecideAsync(request, ct);

        var cacheKey = $"authz:platform:admin:{request.TenantId:N}:{request.SubjectId:N}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async innerCt =>
            {
                var decision = await inner.DecideAsync(request, innerCt);
                return new CachedDecision(decision.State, decision.Reason);
            },
            CacheOptions,
            ct) switch
        {
            { } cached => new AuthorizationDecision(cached.State, cached.Reason),
            null => new AuthorizationDecision(AuthorizationDecisionState.Deny, "Cache unavailable")
        };
    }

    private static bool IsPlatformAdminCheck(AuthorizationRequest request) =>
        request.Action == "platform.manage" && request.Resource == "platform";

    private sealed record CachedDecision(AuthorizationDecisionState State, string? Reason);
}
