namespace SharedKernel.Authorization;

public interface IAuthorizationDecisionService
{
    Task<AuthorizationDecision> DecideAsync(AuthorizationRequest request, CancellationToken ct);
}
