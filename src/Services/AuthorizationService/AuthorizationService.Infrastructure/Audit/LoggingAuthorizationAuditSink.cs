using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AuthorizationService.Infrastructure.Audit;

public sealed class LoggingAuthorizationAuditSink(ILogger<LoggingAuthorizationAuditSink> logger) : IAuthorizationAuditSink
{
    public Task RecordDecisionAsync(AuthorizationDecisionInput input, AuthorizationDecisionResult result, CancellationToken cancellationToken)
    {
        logger.LogInformation("Authorization decision tenant={TenantId} subject={SubjectId} action={Action} resource={ResourceType}/{ResourceId} allowed={Allowed} reason={ReasonCode} correlation={CorrelationId}", input.TenantId, input.SubjectId.Value, input.Action.Value, input.Resource.ResourceType, input.Resource.ResourceId, result.IsAllowed, result.ReasonCode, result.CorrelationId);
        return Task.CompletedTask;
    }

    public Task RecordStateChangeAsync(string eventName, Guid tenantId, Guid correlationId, Guid? subjectId, bool succeeded, string? details, CancellationToken cancellationToken)
    {
        logger.LogInformation("Authorization state change event={EventName} tenant={TenantId} subject={SubjectId} succeeded={Succeeded} details={Details} correlation={CorrelationId}", eventName, tenantId, subjectId, succeeded, details, correlationId);
        return Task.CompletedTask;
    }
}
