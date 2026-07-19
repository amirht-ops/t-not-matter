using AuthorizationService.Domain.Models;

namespace AuthorizationService.Application.Common.Abstractions;

public interface IAuthorizationAuditSink
{
    Task RecordDecisionAsync(AuthorizationDecisionInput input, AuthorizationDecisionResult result, CancellationToken cancellationToken);
    Task RecordStateChangeAsync(string eventName, Guid tenantId, Guid correlationId, Guid? subjectId, bool succeeded, string? details, CancellationToken cancellationToken);
}
