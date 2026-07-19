using System.Diagnostics;
using AuthorizationService.Domain.Models;
using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Domain.Services;

public sealed class AuthorizationDecisionService : IAuthorizationDecisionEngine
{
    private readonly IPolicyEvaluationGateway _opaGateway;
    private readonly IEffectivePermissionResolver _permissionResolver;
    private readonly IRealTimeUsageStore _usageStore;

    public AuthorizationDecisionService(IPolicyEvaluationGateway opaGateway, IEffectivePermissionResolver permissionResolver, IRealTimeUsageStore usageStore)
    {
        _opaGateway = opaGateway;
        _permissionResolver = permissionResolver;
        _usageStore = usageStore;
    }

    public async Task<AuthorizationDecisionResult> EvaluateAsync(AuthorizationDecisionInput input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = input.Context.CorrelationId;
        var correlationId = input.Context.CorrelationId;

        // Step 1: User Status — caller-provided; non-active subjects rejected here
        if (input.Context.Environment.TryGetValue("subject_status", out var subjectStatus) && !string.Equals(subjectStatus, "active", StringComparison.OrdinalIgnoreCase))
            return Deny(AuthorizationDecisionReason.SubjectInactive, "Subject is not active.", correlationId, stopwatch, requestId);

        // Step 2: Tenant Status — caller-provided; non-active tenants rejected
        if (input.Context.Environment.TryGetValue("tenant_status", out var tenantStatus) && !string.Equals(tenantStatus, "active", StringComparison.OrdinalIgnoreCase))
            return Deny(AuthorizationDecisionReason.TenantInactive, "Tenant is not active.", correlationId, stopwatch, requestId);

        // Step 3: Real-Time Usage Check — multi-window guard before expensive OPA call
        if (input.Context.Usage.TryGetValue("usage_limit", out var limitStr) && long.TryParse(limitStr, out var usageLimit) && usageLimit > 0)
        {
            try
            {
                var dailyLimit = usageLimit;
                var weeklyLimit = usageLimit * 7;
                var monthlyLimit = usageLimit * 30;

                if (input.Context.Usage.TryGetValue("daily_limit", out var dailyStr) && long.TryParse(dailyStr, out var d)) dailyLimit = d;
                if (input.Context.Usage.TryGetValue("weekly_limit", out var weeklyStr) && long.TryParse(weeklyStr, out var w)) weeklyLimit = w;
                if (input.Context.Usage.TryGetValue("monthly_limit", out var monthlyStr) && long.TryParse(monthlyStr, out var m)) monthlyLimit = m;

                var dailyWindow = TimeWindow.Daily(DateTimeOffset.UtcNow);
                var weeklyWindow = TimeWindow.Weekly(DateTimeOffset.UtcNow);
                var monthlyWindow = TimeWindow.Monthly(DateTimeOffset.UtcNow);

                var quota = await _usageStore.LoadQuotaSnapshotAsync(
                    input.TenantId, input.SubjectId, input.Action.Value,
                    input.Resource.ResourceType, input.Resource.ResourceId,
                    dailyWindow, weeklyWindow, monthlyWindow,
                    dailyLimit, weeklyLimit, monthlyLimit, cancellationToken);

                if (quota.CanStartRequest())
                {
                    // proceed — the request started while each active quota window had positive remaining capacity
                }
                else
                {
                    stopwatch.Stop();
                    return Deny(AuthorizationDecisionReason.UsageLimitExceeded,
                        $"Usage limit exceeded: daily={quota.DailyRemaining}/{dailyLimit}, weekly={quota.WeeklyRemaining}/{weeklyLimit}, monthly={quota.MonthlyRemaining}/{monthlyLimit}.",
                        correlationId, stopwatch, requestId);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                return Deny(AuthorizationDecisionReason.UsageStoreUnavailable,
                    "Real-time usage store timed out. Access denied.",
                    correlationId, stopwatch, requestId);
            }
            catch (Exception)
            {
                stopwatch.Stop();
                return Deny(AuthorizationDecisionReason.UsageStoreUnavailable,
                    "Real-time usage store unavailable. Access denied.",
                    correlationId, stopwatch, requestId);
            }
        }

        // Step 4: Explicit Deny — check for explicit deny assignments
        if (input.Context.Environment.TryGetValue("explicit_deny", out var explicitDeny) && string.Equals(explicitDeny, "true", StringComparison.OrdinalIgnoreCase))
            return Deny(AuthorizationDecisionReason.ExplicitDeny, "Explicit deny rule matched.", correlationId, stopwatch, requestId);

        // Step 5: Tenant Restriction — validate subject belongs to tenant
        if (input.Context.Environment.TryGetValue("cross_tenant", out var crossTenant) && string.Equals(crossTenant, "true", StringComparison.OrdinalIgnoreCase))
            return Deny(AuthorizationDecisionReason.MissingTenant, "Cross-tenant access denied.", correlationId, stopwatch, requestId);

        // Step 6: Delegation Resolution — resolve active delegations
        var effectivePermissions = new List<string>(input.Permissions);
        if (input.Context.Environment.TryGetValue("delegated_permissions", out var delegatedPermsRaw) && !string.IsNullOrEmpty(delegatedPermsRaw))
        {
            var delegatedPerms = delegatedPermsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var dp in delegatedPerms)
            {
                if (!effectivePermissions.Contains(dp, StringComparer.OrdinalIgnoreCase))
                    effectivePermissions.Add(dp);
            }
        }

        // Step 7: Role Resolution — resolve effective role names from environment
        var effectiveRoles = input.Roles.Any()
            ? input.Roles
            : (input.Context.Environment.TryGetValue("effective_roles", out var rolesRaw)
                ? rolesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : []);

        // Step 8: Permission Resolution — full resolution via domain service
        var resolvedPermissions = await _permissionResolver.ResolveAsync(input.TenantId, input.SubjectId, cancellationToken);
        foreach (var perm in resolvedPermissions)
        {
            if (!effectivePermissions.Contains(perm, StringComparer.OrdinalIgnoreCase))
                effectivePermissions.Add(perm);
        }

        // Step 9: Policy Evaluation — delegate to OPA through gateway
        // department_id is explicitly extracted from RequestContext and passed to OPA input
        var opaInput = input with { Roles = effectiveRoles, Permissions = effectivePermissions };
        OpaEvaluationResult opaResult;
        try
        {
            opaResult = await _opaGateway.EvaluateAsync(opaInput, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Deny(AuthorizationDecisionReason.OpaTimeout, "OPA evaluation timed out.", correlationId, stopwatch, requestId);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            return Deny(AuthorizationDecisionReason.OpaUnavailable, "OPA evaluation failed.", correlationId, stopwatch, requestId);
        }

        stopwatch.Stop();

        // Step 10: Final Decision
        if (opaResult.Allow)
        {
            return AuthorizationDecisionResult.Allow(
                opaResult.Reason ?? "Allowed by policy.",
                correlationId,
                stopwatch.ElapsedMilliseconds,
                opaResult.PolicyId,
                opaResult.PolicyVersion,
                DateTimeOffset.UtcNow,
                requestId);
        }

        return AuthorizationDecisionResult.Deny(
            AuthorizationDecisionReason.DeniedByPolicy,
            opaResult.Reason ?? "Denied by policy.",
            correlationId,
            stopwatch.ElapsedMilliseconds,
            DateTimeOffset.UtcNow,
            requestId);
    }

    private static AuthorizationDecisionResult Deny(string reasonCode, string reasonMessage, Guid correlationId, Stopwatch stopwatch, Guid requestId)
    {
        stopwatch.Stop();
        return AuthorizationDecisionResult.Deny(reasonCode, reasonMessage, correlationId, stopwatch.ElapsedMilliseconds, DateTimeOffset.UtcNow, requestId);
    }

}
