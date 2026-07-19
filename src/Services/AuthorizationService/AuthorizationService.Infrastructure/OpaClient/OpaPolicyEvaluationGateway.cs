using AuthorizationService.Domain.Models;
using AuthorizationService.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AuthorizationService.Infrastructure.OpaClient;

public sealed class OpaPolicyEvaluationGateway(OpaHttpClient opaClient, ILogger<OpaPolicyEvaluationGateway> logger) : IPolicyEvaluationGateway
{
    public async Task<OpaEvaluationResult> EvaluateAsync(AuthorizationDecisionInput input, CancellationToken cancellationToken)
    {
        var response = await opaClient.EvaluateAsync(new OpaRequest(ToOpaInput(input)), cancellationToken);
        if (response?.Result is null)
        {
            logger.LogWarning("OPA returned no result tenant={TenantId} correlation={CorrelationId}", input.TenantId, input.Context.CorrelationId);
            return new OpaEvaluationResult(false, "OPA did not return a decision.", null, null);
        }
        return new OpaEvaluationResult(response.Result.Allow, response.Result.Reason, response.Result.PolicyId, response.Result.PolicyVersion);
    }

    private static object ToOpaInput(AuthorizationDecisionInput input) => new
    {
        tenant_id = input.TenantId,
        department_id = input.DepartmentId,
        subject = new { id = input.SubjectId.Value, roles = input.Roles, permissions = input.Permissions },
        action = input.Action.Value,
        resource = new { type = input.Resource.ResourceType, id = input.Resource.ResourceId, ownerId = input.Resource.OwnerId, attributes = input.Resource.Attributes },
        context = new { ip_address = input.Context.IpAddress, user_agent = input.Context.UserAgent, environment = input.Context.Environment, usage = input.Context.Usage, correlation_id = input.Context.CorrelationId, request_time = input.Context.RequestTime },
        effective_permissions = input.Permissions,
        evaluation_timestamp = DateTimeOffset.UtcNow,
        correlation_id = input.Context.CorrelationId
    };
}
