namespace Platform.Abstractions.Authorization;

public static class PlatformAuthorizationPolicies
{
    public const string PlatformServiceOnly = "PlatformServiceOnly";

    public const string RequireIdentityService = "RequireService.Identity";
    public const string RequireTenantService = "RequireService.Tenant";
    public const string RequireAuthorizationService = "RequireService.Authorization";
    public const string RequireAuditService = "RequireService.Audit";
    public const string RequirePolicyService = "RequireService.Policy";
    public const string RequireSchedulerService = "RequireService.Scheduler";
    public const string RequireOpaService = "RequireService.OPA";
    public const string RequireNotificationService = "RequireService.Notification";
    
    public const string AllowIdentityAndAuthorization = "AllowIdentityAndAuthorization";
}
