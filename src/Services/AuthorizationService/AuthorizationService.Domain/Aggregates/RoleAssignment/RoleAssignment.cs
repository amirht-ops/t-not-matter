using AuthorizationService.Domain.Errors;
using AuthorizationService.Domain.Events;
using AuthorizationService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace AuthorizationService.Domain.Aggregates.RoleAssignment;

public sealed class RoleAssignment : AggregateRoot
{
    private RoleAssignment() { }

    private RoleAssignment(Guid id, Guid tenantId, SubjectId subjectId, RoleId roleId, Guid departmentId, UserId assignedBy) : base(id, tenantId)
    {
        SubjectId = subjectId;
        RoleId = roleId;
        DepartmentId = departmentId;
        AssignedBy = assignedBy;
        AssignedAtUtc = DateTimeOffset.UtcNow;
    }

    public SubjectId SubjectId { get; private init; }
    public RoleId RoleId { get; private init; }
    public Guid DepartmentId { get; private init; }
    public UserId AssignedBy { get; private init; }
    public DateTimeOffset AssignedAtUtc { get; private init; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public bool IsActive => RevokedAtUtc is null;

    public static RoleAssignment Assign(Guid tenantId, SubjectId subjectId, RoleId roleId, Guid departmentId, UserId assignedBy, Guid correlationId)
    {
        var assignment = new RoleAssignment(Guid.NewGuid(), tenantId, subjectId, roleId, departmentId, assignedBy);
        assignment.RaiseDomainEvent(new RoleAssignedDomainEvent(assignment.Id, subjectId.Value, roleId.Value, tenantId, departmentId, correlationId));
        return assignment;
    }

    public void Revoke(Guid correlationId)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = DateTimeOffset.UtcNow;
        MarkUpdated();
        RaiseDomainEvent(new RoleRevokedDomainEvent(Id, SubjectId.Value, RoleId.Value, TenantId, correlationId));
    }

    public Result<Unit> EnsureTenantMatch(Guid expectedTenantId)
    {
        if (TenantId != expectedTenantId)
            return Result<Unit>.Failure(AuthorizationErrors.AssignmentTenantMismatch);

        return Result<Unit>.Success(Unit.Value);
    }
}
