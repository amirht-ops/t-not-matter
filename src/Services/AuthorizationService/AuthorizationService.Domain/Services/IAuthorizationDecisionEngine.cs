using AuthorizationService.Domain.Models;

namespace AuthorizationService.Domain.Services;

public interface IAuthorizationDecisionEngine
{
    Task<AuthorizationDecisionResult> EvaluateAsync(AuthorizationDecisionInput input, CancellationToken cancellationToken);
}
