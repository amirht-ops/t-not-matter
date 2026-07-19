using AuthorizationService.Application.Features.EvaluateAuthorizationDecision;
using AuthorizationService.Domain.Models;
using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.EvaluateBatchAuthorizationDecisions;

public sealed class EvaluateBatchAuthorizationDecisionsCommandHandler(ISender sender, IRequestContextAccessor requestContext) : IRequestHandler<EvaluateBatchAuthorizationDecisionsCommand, Result<EvaluateBatchAuthorizationDecisionsResponse>>
{
    public async Task<Result<EvaluateBatchAuthorizationDecisionsResponse>> Handle(EvaluateBatchAuthorizationDecisionsCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        if (context.TenantId == Guid.Empty)
        {
            return Result<EvaluateBatchAuthorizationDecisionsResponse>.Success(new EvaluateBatchAuthorizationDecisionsResponse(command.Decisions.Select(item => new EvaluateAuthorizationDecisionResponse(false, AuthorizationDecisionState.Deny.ToString(), AuthorizationDecisionReason.MissingTenant, "Tenant context is missing.", null, null, context.CorrelationId, DateTimeOffset.UtcNow, 0, context.RequestId)).ToArray()));
        }

        var results = new List<EvaluateAuthorizationDecisionResponse>(command.Decisions.Count);
        foreach (var decision in command.Decisions)
        {
            try
            {
                var result = await sender.Send(decision with { IpAddress = command.IpAddress, UserAgent = command.UserAgent }, cancellationToken);
                results.Add(result.IsSuccess
                    ? result.Value!
                    : new EvaluateAuthorizationDecisionResponse(false, AuthorizationDecisionState.Deny.ToString(),
                        AuthorizationDecisionReason.InvalidInput, result.Error.Description, null, null,
                        context.CorrelationId, DateTimeOffset.UtcNow, 0, context.RequestId));
            }
            catch
            {
                results.Add(new EvaluateAuthorizationDecisionResponse(false, AuthorizationDecisionState.Deny.ToString(),
                    AuthorizationDecisionReason.EvaluationError, "Authorization evaluation failed.", null, null,
                    context.CorrelationId, DateTimeOffset.UtcNow, 0, context.RequestId));
            }
        }

        return Result<EvaluateBatchAuthorizationDecisionsResponse>.Success(
            new EvaluateBatchAuthorizationDecisionsResponse(results));
    }
}