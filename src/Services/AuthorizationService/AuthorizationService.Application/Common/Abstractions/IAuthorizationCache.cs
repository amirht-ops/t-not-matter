using AuthorizationService.Domain.Models;
using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Application.Common.Abstractions;

public interface IAuthorizationCache
{
    Task<IReadOnlyCollection<string>?> GetEffectivePermissionsAsync(Guid tenantId, SubjectId subjectId, CancellationToken cancellationToken);
    Task SetEffectivePermissionsAsync(Guid tenantId, SubjectId subjectId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);
    Task<AuthorizationDecisionResult?> GetDecisionAsync(Guid tenantId, string decisionHash, CancellationToken cancellationToken);
    Task SetDecisionAsync(Guid tenantId, string decisionHash, AuthorizationDecisionResult decision, CancellationToken cancellationToken);
    Task InvalidateSubjectAsync(Guid tenantId, SubjectId subjectId, CancellationToken cancellationToken);
    Task InvalidateTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
