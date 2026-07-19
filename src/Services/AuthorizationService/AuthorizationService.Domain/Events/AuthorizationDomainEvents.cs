using AuthorizationService.Domain.ValueObjects;
using SharedKernel.Domain.Events;

namespace AuthorizationService.Domain.Events;

public abstract record AuthorizationDomainEvent(Guid TenantId, Guid CorrelationId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public Guid? CausationId { get; }
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    public int Version { get; } = 1;
    public abstract string EventTypeName { get; }
}

public sealed record RoleCreatedDomainEvent(Guid RoleId, Guid TenantIdValue, Guid DepartmentIdValue, string Name, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.role-created.v1";
}

public sealed record RoleActivatedDomainEvent(Guid RoleId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.role-activated.v1";
}

public sealed record RoleDeactivatedDomainEvent(Guid RoleId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.role-deactivated.v1";
}

public sealed record RoleDisabledDomainEvent(Guid RoleId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.role-disabled.v1";
}

public sealed record RoleParentChangedDomainEvent(Guid RoleId, Guid TenantIdValue, Guid? NewParentRoleId, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.role-parent-changed.v1";
}

public sealed record RoleAssignedDomainEvent(Guid AssignmentId, Guid SubjectIdValue, Guid RoleId, Guid TenantIdValue, Guid DepartmentIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.role-assigned.v1";
}

public sealed record RoleRevokedDomainEvent(Guid AssignmentId, Guid SubjectIdValue, Guid RoleId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.role-revoked.v1";
}

public sealed record PermissionCreatedDomainEvent(Guid PermissionId, Guid TenantIdValue, string Key, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-created.v1";
}

public sealed record PermissionSubmittedForReviewDomainEvent(Guid PermissionId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-submitted-for-review.v1";
}

public sealed record PermissionApprovedDomainEvent(Guid PermissionId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-approved.v1";
}

public sealed record PermissionPublishedDomainEvent(Guid PermissionId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-published.v1";
}

public sealed record PermissionDeprecatedDomainEvent(Guid PermissionId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-deprecated.v1";
}

public sealed record PermissionArchivedDomainEvent(Guid PermissionId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-archived.v1";
}

public sealed record PermissionVersionCreatedDomainEvent(Guid PermissionId, Guid TenantIdValue, int PermissionVersion, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-version-created.v1";
}

public sealed record PermissionGrantedDomainEvent(Guid GrantId, Guid RoleId, Guid PermissionId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-granted.v1";
}

public sealed record PermissionRevokedDomainEvent(Guid GrantId, Guid RoleId, Guid PermissionId, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.permission-revoked.v1";
}

public sealed record AuthorizationEvaluatedDomainEvent(Guid DecisionId, Guid SubjectIdValue, string Action, string ResourceType, string ResourceId, bool IsAllowed, string ReasonCode, Guid TenantIdValue, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.evaluated.v1";
}

public sealed record UsageTrackingCreatedDomainEvent(Guid TrackingId, Guid TenantIdValue, Guid SubjectIdValue, string Action, string ResourceType, string ResourceId, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.usage-tracking-created.v1";
}

public sealed record UsageIncrementedDomainEvent(Guid TrackingId, Guid TenantIdValue, Guid SubjectIdValue, string Action, string ResourceType, string ResourceId, long NewCount, Guid CorrelationIdValue) : AuthorizationDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "authorization.usage-incremented.v1";
}
