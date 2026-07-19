using AuthorizationService.Domain.Errors;
using AuthorizationService.Domain.Events;
using AuthorizationService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace AuthorizationService.Domain.Aggregates.PermissionGrant;

public sealed class PermissionGrant : AggregateRoot
{
    private PermissionGrant() { }

    private PermissionGrant(Guid id, Guid tenantId, RoleId roleId, PermissionId permissionId, UserId grantedBy) : base(id, tenantId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        GrantedBy = grantedBy;
        GrantedAtUtc = DateTimeOffset.UtcNow;
    }

    public RoleId RoleId { get; private init; }
    public PermissionId PermissionId { get; private init; }
    public UserId GrantedBy { get; private init; }
    public DateTimeOffset GrantedAtUtc { get; private init; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public bool IsActive => RevokedAtUtc is null;

    public static PermissionGrant Grant(Guid tenantId, RoleId roleId, PermissionId permissionId, UserId grantedBy, Guid correlationId)
    {
        var grant = new PermissionGrant(Guid.NewGuid(), tenantId, roleId, permissionId, grantedBy);
        grant.RaiseDomainEvent(new PermissionGrantedDomainEvent(grant.Id, roleId.Value, permissionId.Value, tenantId, correlationId));
        return grant;
    }

    public void Revoke(Guid correlationId)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = DateTimeOffset.UtcNow;
        MarkUpdated();
        RaiseDomainEvent(new PermissionRevokedDomainEvent(Id, RoleId.Value, PermissionId.Value, TenantId, correlationId));
    }

    public Result<Unit> EnsureTenantMatch(Guid expectedTenantId)
    {
        if (TenantId != expectedTenantId)
            return Result<Unit>.Failure(AuthorizationErrors.GrantTenantMismatch);

        return Result<Unit>.Success(Unit.Value);
    }
}
