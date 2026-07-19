using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Domain.Models;

public sealed record AuthorizationDecisionInput(
    Guid TenantId,
    SubjectId SubjectId,
    AuthorizationAction Action,
    ResourceDescriptor Resource,
    AuthorizationContext Context,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    Guid? DepartmentId = null)
{
    public bool IsComplete => TenantId != Guid.Empty && SubjectId.Value != Guid.Empty && Context.CorrelationId != Guid.Empty;
}
