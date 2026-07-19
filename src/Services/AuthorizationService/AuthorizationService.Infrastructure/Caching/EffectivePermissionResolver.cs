using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.Services;
using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Infrastructure.Caching;

public sealed class EffectivePermissionResolver(IPermissionRepository permissions, IRoleAssignmentRepository assignments, IAuthorizationCache cache) : IEffectivePermissionResolver
{
    public async Task<IReadOnlyCollection<string>> ResolveAsync(Guid tenantId, SubjectId subjectId, CancellationToken cancellationToken)
    {
        var cached = await cache.GetEffectivePermissionsAsync(tenantId, subjectId, cancellationToken);
        if (cached is not null) return cached;
        var roleIds = await assignments.GetActiveRoleIdsIncludingInheritedAsync(tenantId, subjectId, cancellationToken: cancellationToken);
        var effective = await permissions.GetActivePermissionKeysForRolesAsync(tenantId, roleIds, cancellationToken: cancellationToken);
        await cache.SetEffectivePermissionsAsync(tenantId, subjectId, effective, cancellationToken);
        return effective;
    }
}
