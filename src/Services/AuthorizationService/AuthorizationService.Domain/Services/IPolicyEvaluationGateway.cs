using AuthorizationService.Domain.Models;

namespace AuthorizationService.Domain.Services;

public sealed record OpaEvaluationResult(bool Allow, string? Reason, string? PolicyId, string? PolicyVersion);

public interface IPolicyEvaluationGateway
{
    Task<OpaEvaluationResult> EvaluateAsync(AuthorizationDecisionInput input, CancellationToken cancellationToken);
}
