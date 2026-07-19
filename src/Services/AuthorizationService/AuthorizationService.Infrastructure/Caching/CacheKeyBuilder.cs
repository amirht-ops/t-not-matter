using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Infrastructure.Caching;

public static class CacheKeyBuilder
{
    public static string EffectivePermissions(Guid tenantId, SubjectId subjectId) => $"authz:{tenantId}:subject:{subjectId.Value}:effective-permissions";
    public static string Decision(Guid tenantId, string hash) => $"authz:{tenantId}:decision:{hash}";
}
