using Platform.Abstractions.Principal;
using Platform.Abstractions.Tenant;

namespace Platform.Middleware;

public sealed class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IRequestContextAccessor contextAccessor, ICurrentPrincipalAccessor principalAccessor)
    {
        var principal = principalAccessor.Principal;

        var correlationId = context.Items.TryGetValue("CorrelationId", out var cidObj) && cidObj is Guid cid
            ? cid
            : Guid.NewGuid();

        var requestId = context.Items.TryGetValue("RequestId", out var ridObj) && ridObj is Guid rid
            ? rid
            : Guid.NewGuid();

        if (principal.IsService)
        {
            var serviceTenantId = TryGetTenantOverride(context, principal) ?? principal.TenantId ?? Guid.Empty;

            contextAccessor.Context = new RequestContext(
                serviceTenantId,
                correlationId,
                requestId,
                null,
                null,
                PrincipalType.Service,
                principal.ServiceName,
                principal.CanBypassTenantIsolation);

            context.Items["CorrelationId"] = correlationId;
            context.Items["RequestId"] = requestId;
            context.Items["TenantId"] = serviceTenantId;
            context.Items["CurrentPrincipal"] = principal;

            await next(context);
            return;
        }

        var tenantId = Guid.Empty;
        Guid? userId = null;
        var canBypass = !principal.IsAuthenticated;

        if (principal.IsUser)
        {
            tenantId = TryGetTenantOverride(context, principal) ?? principal.TenantId ?? Guid.Empty;
            userId = principal.UserId;
            canBypass = principal.CanBypassTenantIsolation;
        }

        contextAccessor.Context = new RequestContext(
            tenantId,
            correlationId,
            requestId,
            userId,
            principal.IsUser ? principal.DepartmentId : null,
            principal.IsUser ? PrincipalType.User : PrincipalType.Unknown,
            null,
            canBypass);

        context.Items["CorrelationId"] = correlationId;
        context.Items["RequestId"] = requestId;
        context.Items["TenantId"] = tenantId;
        context.Items["CurrentPrincipal"] = principal;

        await next(context);
    }

    private static Guid? TryGetTenantOverride(HttpContext context, ICurrentPrincipal principal)
    {
        if (!principal.CanBypassTenantIsolation)
            return null;

        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValues) &&
            Guid.TryParse(headerValues.ToString(), out var headerTenantId) &&
            headerTenantId != Guid.Empty)
            return headerTenantId;

        return null;
    }
}