namespace SharedKernel.Authorization;

public enum AuthorizationDecisionState
{
    Allow = 1,
    Deny = 2,
    NotApplicable = 3,
    Indeterminate = 4
}
