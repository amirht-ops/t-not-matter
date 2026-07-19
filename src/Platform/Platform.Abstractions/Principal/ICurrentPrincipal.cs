using System.Security.Claims;

namespace Platform.Abstractions.Principal;

public interface ICurrentPrincipal
{
    bool IsAuthenticated { get; }
    bool IsUser { get; }
    bool IsService { get; }
    bool CanBypassTenantIsolation { get; }
    PrincipalType Type { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }
    Guid? DepartmentId { get; }
    Guid? RoleId { get; }
    string? ServiceName { get; }
    ClaimsPrincipal? ClaimsPrincipal { get; }
}
