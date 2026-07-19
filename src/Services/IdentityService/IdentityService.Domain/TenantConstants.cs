namespace IdentityService.Domain;

/// <summary>
/// Well-known tenant identifier used for users that have not yet been bound to a
/// concrete tenant. Public self-registration creates users in this "global" tenant
/// in a Pending state; the real TenantId is assigned later when an administrator
/// grants the user their first role (admin onboarding).
/// </summary>
public static class TenantConstants
{
    public static readonly Guid GlobalTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
}
