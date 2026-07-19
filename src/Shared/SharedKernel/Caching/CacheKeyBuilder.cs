using System.Text;

namespace SharedKernel.Caching;

public static partial class CacheKeyBuilder
{
    public static string TenantSlug(string slug) => $"tenant:slug:{slug}";

    public static string TenantMetadata(Guid tenantId) => $"tenant:metadata:{tenantId:N}";

    public static string TenantConfig(Guid tenantId) => $"tenant:config:{tenantId:N}";

    public static string TenantFeature(Guid tenantId) => $"tenant:feature:{tenantId:N}";

    public static string TenantLimit(Guid tenantId) => $"tenant:limit:{tenantId:N}";

    public static string AuthorizationDecision(Guid tenantId, string hash) => $"authz:decision:{tenantId:N}:{hash}";

    public static string EffectivePermissions(Guid tenantId, string subjectId) => $"authz:permissions:{tenantId:N}:{subjectId}";

    public static string PolicyEvaluation(Guid tenantId, string policyId) => $"policy:evaluation:{tenantId:N}:{policyId}";

    public static string Session(Guid tenantId, string sessionId) => $"session:{tenantId:N}:{sessionId}";

    public static string DistributedLock(string name) => $"lock:{name}";

    public static string Idempotency(string key) => $"idempotency:{key}";

    public static string RateLimit(string policyName, string key) => $"ratelimit:{policyName}:{key}";

    public static string Combine(params string[] segments)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0) sb.Append(':');
            sb.Append(segments[i]);
        }
        return sb.ToString();
    }
}
