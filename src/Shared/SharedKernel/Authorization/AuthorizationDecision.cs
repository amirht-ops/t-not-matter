namespace SharedKernel.Authorization;

public sealed record AuthorizationDecision(AuthorizationDecisionState State, string? Reason = null)
{
    public bool IsAllowed => State == AuthorizationDecisionState.Allow;
}
