namespace IdentityService.Application.Common.Abstractions;

public static class IdentityAuthorizationActions
{
    public const string ActivateUser = "Identity.User.Activate";
    public const string UnlockUser = "Identity.User.Unlock";
    public const string DisableUser = "Identity.User.Disable";
    public const string DeleteUser = "Identity.User.Delete";
    public const string EnableMfa = "Identity.Mfa.Enable";
    public const string Logout = "Identity.Session.Logout";
}
