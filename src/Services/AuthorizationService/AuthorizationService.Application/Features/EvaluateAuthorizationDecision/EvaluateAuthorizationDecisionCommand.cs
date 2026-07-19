using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.EvaluateAuthorizationDecision;

public sealed record EvaluateAuthorizationDecisionCommand(
    Guid? SubjectId,
    string Action,
    string ResourceType,
    string ResourceId,
    Guid? OwnerId,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    IReadOnlyDictionary<string, string> EnvironmentAttributes,
    IReadOnlyDictionary<string, string> UsageAttributes,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<EvaluateAuthorizationDecisionResponse>>, IRetryableRequest, IAuthorizableRequest
{
    string IAuthorizableRequest.Action => "authorization.evaluate";
    public string Resource => $"resource:{ResourceType}:{ResourceId}";
}
