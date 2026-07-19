using System.Security.Claims;
using Platform.Abstractions.Principal;

namespace Platform.Infrastructure.Principal;

public sealed class CurrentPrincipalFactory(IPlatformServiceRegistry serviceRegistry)
    : ICurrentPrincipalFactory
{
    public ICurrentPrincipal CreateFromClaimsPrincipal(ClaimsPrincipal user)
    {
        if (user.Identity is not { IsAuthenticated: true })
            return CurrentPrincipal.Unauthenticated;

        var principalTypeClaim = user.FindFirst("principal_type")?.Value;
        var serviceNameClaim = user.FindFirst("service_name")?.Value;
        var subClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? user.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(subClaim) && !string.IsNullOrWhiteSpace(serviceNameClaim))
        {
            principalTypeClaim = "service";
        }

        if (string.Equals(principalTypeClaim, "service", StringComparison.OrdinalIgnoreCase))
        {
            Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId);
            var serviceName = serviceNameClaim ?? "unknown";
            // Normalize resolves backward-compatible kebab-case aliases to canonical PascalCase.
            // Returns null for unknown services (fail-closed: no RLS bypass for unregistered callers).
            var canonicalName = serviceRegistry.Normalize(serviceName);
            return new CurrentPrincipal
            {
                IsAuthenticated = true,
                Type = PrincipalType.Service,
                ServiceName = canonicalName ?? serviceName,
                TenantId = tenantId == Guid.Empty ? null : tenantId,
                CanBypassTenantIsolation = canonicalName is not null,
                ClaimsPrincipal = user
            };
        }

        if (string.Equals(principalTypeClaim, "user", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(subClaim))
        {
            Guid.TryParse(subClaim, out var userId);
            Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId);
            Guid.TryParse(user.FindFirst("department_id")?.Value, out var departmentId);
            Guid.TryParse(user.FindFirst("role_id")?.Value, out var roleId);
            var canBypass = user.HasClaim("platform_admin", "true");
            return new CurrentPrincipal
            {
                IsAuthenticated = true,
                Type = PrincipalType.User,
                UserId = userId == Guid.Empty ? null : userId,
                TenantId = tenantId == Guid.Empty ? null : tenantId,
                DepartmentId = departmentId == Guid.Empty ? null : departmentId,
                RoleId = roleId == Guid.Empty ? null : roleId,
                CanBypassTenantIsolation = canBypass,
                ClaimsPrincipal = user
            };
        }

        return CurrentPrincipal.Unauthenticated;
    }

    public ICurrentPrincipal CreateServicePrincipal(PlatformServiceName service)
    {
        return new CurrentPrincipal
        {
            IsAuthenticated = true,
            Type = PrincipalType.Service,
            ServiceName = service.Value,
            CanBypassTenantIsolation = true,
        };
    }

    public ICurrentPrincipal CreateSystemPrincipal()
    {
        return new CurrentPrincipal
        {
            IsAuthenticated = true,
            Type = PrincipalType.Service,
            ServiceName = "System",
            CanBypassTenantIsolation = true,
        };
    }

    public ICurrentPrincipal CreateUnauthenticated() => CurrentPrincipal.Unauthenticated;
}