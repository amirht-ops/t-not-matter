using AuthorizationService.Application.Features.EvaluateAuthorizationDecision;
using MediatR;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.EvaluateBatchAuthorizationDecisions;

public sealed record EvaluateBatchAuthorizationDecisionsCommand(
    IReadOnlyCollection<EvaluateAuthorizationDecisionCommand> Decisions,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<EvaluateBatchAuthorizationDecisionsResponse>>, IAuthorizableRequest
{
    public string Action => "authorization.batch.evaluate";
    public string Resource => "authorization:batch";
}
