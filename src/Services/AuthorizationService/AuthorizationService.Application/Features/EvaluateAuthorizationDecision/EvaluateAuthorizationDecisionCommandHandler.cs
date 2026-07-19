using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Results;
using AuthorizationService.Domain.Services;
using AuthorizationService.Domain.Models;
using AuthorizationService.Domain.ValueObjects;
using Platform.Abstractions.Tenant;

namespace AuthorizationService.Application.Features.EvaluateAuthorizationDecision;

public sealed class EvaluateAuthorizationDecisionCommandHandler : IRequestHandler<EvaluateAuthorizationDecisionCommand, Result<EvaluateAuthorizationDecisionResponse>>
{
    private readonly IRequestContextAccessor _requestContext;
    private readonly IAuthorizationDecisionEngine _decisionEngine;

    public EvaluateAuthorizationDecisionCommandHandler(
        IRequestContextAccessor requestContext,
        IAuthorizationDecisionEngine decisionEngine)
    {
        _requestContext = requestContext;
        _decisionEngine = decisionEngine;
    }

    public async Task<Result<EvaluateAuthorizationDecisionResponse>> Handle(EvaluateAuthorizationDecisionCommand request, CancellationToken cancellationToken)
    {
        var ctx = _requestContext.Context;
        var tenantId = ctx.TenantId;
        var subjectId = request.SubjectId is not null && request.SubjectId.Value != Guid.Empty
            ? SubjectId.From(request.SubjectId.Value)
            : SubjectId.Anonymous;
        var action = AuthorizationAction.Create(request.Action);
        var resource = ResourceDescriptor.Create(request.ResourceType, request.ResourceId, request.OwnerId, request.ResourceAttributes);

        var envWithMeta = new Dictionary<string, string>(request.EnvironmentAttributes)
        {
            ["request_id"] = ctx.RequestId.ToString()
        };

        var context = AuthorizationContext.Create(
            request.IpAddress,
            request.UserAgent,
            envWithMeta,
            request.UsageAttributes,
            ctx.CorrelationId,
            DateTime.UtcNow);

        var input = new AuthorizationDecisionInput(
            tenantId, subjectId, action, resource, context, [], [], ctx.DepartmentId);

        var result = await _decisionEngine.EvaluateAsync(input, cancellationToken);

        return Result<EvaluateAuthorizationDecisionResponse>.Success(Map(result));
    }

    private static EvaluateAuthorizationDecisionResponse Map(AuthorizationDecisionResult d) =>
        new(d.IsAllowed, d.Decision.ToString(), d.ReasonCode, d.ReasonMessage, d.PolicyId, d.PolicyVersion, d.CorrelationId, d.EvaluatedAtUtc, d.EvaluationDurationMs, d.RequestId);
}
