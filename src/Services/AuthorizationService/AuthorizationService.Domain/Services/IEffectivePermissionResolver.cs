using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Domain.Services;

public interface IEffectivePermissionResolver
{
    Task<IReadOnlyCollection<string>> ResolveAsync(Guid tenantId, SubjectId subjectId, CancellationToken cancellationToken);
}
