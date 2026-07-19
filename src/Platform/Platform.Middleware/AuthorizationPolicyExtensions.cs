using Microsoft.Extensions.DependencyInjection;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Principal;

namespace Platform.Middleware;

public static class AuthorizationPolicyExtensions
{
    public static IServiceCollection AddPlatformAuthorizationPolicies(
        this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PlatformAuthorizationPolicies.PlatformServiceOnly, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") ||
                    ctx.User.HasClaim("platform_admin", "true")));

            options.AddPolicy(PlatformAuthorizationPolicies.RequireIdentityService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Identity.Value)));

            options.AddPolicy(PlatformAuthorizationPolicies.RequireTenantService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Tenant.Value)));

            options.AddPolicy(PlatformAuthorizationPolicies.RequireAuthorizationService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Authorization.Value)));

            options.AddPolicy(PlatformAuthorizationPolicies.RequireAuditService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Audit.Value)));

            options.AddPolicy(PlatformAuthorizationPolicies.RequirePolicyService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Policy.Value)));

            options.AddPolicy(PlatformAuthorizationPolicies.RequireSchedulerService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Scheduler.Value)));

            options.AddPolicy(PlatformAuthorizationPolicies.RequireOpaService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Opa.Value)));

            options.AddPolicy(PlatformAuthorizationPolicies.RequireNotificationService, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("principal_type", "service") &&
                    ctx.User.HasClaim("service_name", PlatformServiceName.Notification.Value)));
            
            options.AddPolicy(PlatformAuthorizationPolicies.AllowIdentityAndAuthorization, policy =>
                policy.RequireAssertion(ctx =>
                    (ctx.User.HasClaim("principal_type", "service") &&
                     (ctx.User.HasClaim("service_name", PlatformServiceName.Identity.Value) ||
                      ctx.User.HasClaim("service_name", PlatformServiceName.Authorization.Value))) ||
                    ctx.User.HasClaim("platform_admin", "true")));
        });

        return services;
    }
}
