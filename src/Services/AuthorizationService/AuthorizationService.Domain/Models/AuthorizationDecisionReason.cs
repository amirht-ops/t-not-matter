namespace AuthorizationService.Domain.Models;

public static class AuthorizationDecisionReason
{
    public const string Allowed = "Allowed";
    public const string DeniedByPolicy = "DeniedByPolicy";
    public const string OpaUnavailable = "OpaUnavailable";
    public const string OpaTimeout = "OpaTimeout";
    public const string InvalidInput = "InvalidInput";
    public const string MissingTenant = "MissingTenant";
    public const string EvaluationError = "EvaluationError";
    public const string NotApplicable = "NotApplicable";
    public const string Indeterminate = "Indeterminate";
    public const string ExplicitDeny = "ExplicitDeny";
    public const string DelegationDenied = "DelegationDenied";
    public const string UsageLimitExceeded = "UsageLimitExceeded";
    public const string UsageStoreUnavailable = "UsageStoreUnavailable";
    public const string TenantInactive = "TenantInactive";
    public const string SubjectInactive = "SubjectInactive";
}
