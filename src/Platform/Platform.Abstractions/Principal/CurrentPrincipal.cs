using System.Security.Claims;

namespace Platform.Abstractions.Principal;

public sealed record CurrentPrincipal : ICurrentPrincipal
{
    public static readonly ICurrentPrincipal Unauthenticated = new CurrentPrincipal
    {
        IsAuthenticated = false,
        Type = PrincipalType.Unknown
    };

    public bool IsAuthenticated { get; init; }
    public bool IsUser => IsAuthenticated && Type == PrincipalType.User;
    public bool IsService => IsAuthenticated && Type == PrincipalType.Service;
    public bool CanBypassTenantIsolation { get; init; }
    public PrincipalType Type { get; init; }
    public Guid? UserId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? RoleId { get; init; }
    public string? ServiceName { get; init; }
    public ClaimsPrincipal? ClaimsPrincipal { get; init; }
}
