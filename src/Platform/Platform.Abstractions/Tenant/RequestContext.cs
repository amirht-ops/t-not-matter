using Platform.Abstractions.Principal;

namespace Platform.Abstractions.Tenant;

public sealed record RequestContext(
    Guid TenantId,
    Guid CorrelationId,
    Guid RequestId,
    Guid? UserId = null,
    Guid? DepartmentId = null,
    PrincipalType PrincipalType = PrincipalType.Unknown,
    string? ServiceName = null,
    bool CanBypassTenantIsolation = false)
{
    public bool IsService => PrincipalType == PrincipalType.Service;
    public bool IsUser => PrincipalType == PrincipalType.User;
}
