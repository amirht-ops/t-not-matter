using AuthorizationService.Domain.Errors;
using AuthorizationService.Domain.Events;
using AuthorizationService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace AuthorizationService.Domain.Aggregates.Role;

public sealed class Role : AggregateRoot
{
    public const int MaxHierarchyDepth = 5;
    public const int MaxRolesPerTenant = 100;
    public const int MaxPermissionsPerRole = 200;

    private Role() { }

    private Role(Guid id, Guid tenantId, Guid departmentId, RoleName name, string? description, RoleId? parentRoleId, int hierarchyDepth) : base(id, tenantId)
    {
        DepartmentId = departmentId;
        Name = name;
        Description = description;
        ParentRoleId = parentRoleId;
        HierarchyDepth = hierarchyDepth;
        Status = RoleStatus.Active;
    }

    public Guid DepartmentId { get; private init; }
    public RoleName Name { get; private set; }
    public string? Description { get; private set; }
    public RoleStatus Status { get; private set; }
    public RoleId? ParentRoleId { get; private set; }
    public int HierarchyDepth { get; private set; }
    public bool IsActive => Status == RoleStatus.Active;

    public static Result<Role> Create(Guid tenantId, Guid departmentId, RoleName name, string? description, RoleId? parentRoleId, int currentRoleCount, int? parentHierarchyDepth, Guid correlationId)
    {
        if (currentRoleCount >= MaxRolesPerTenant)
            return Result<Role>.Failure(AuthorizationErrors.MaxRolesPerTenantExceeded);

        var hierarchyDepth = parentRoleId is not null ? (parentHierarchyDepth ?? 0) + 1 : 0;
        if (hierarchyDepth > MaxHierarchyDepth)
            return Result<Role>.Failure(AuthorizationErrors.MaxHierarchyDepthExceeded);

        var role = new Role(Guid.NewGuid(), tenantId, departmentId, name, description, parentRoleId, hierarchyDepth);
        role.RaiseDomainEvent(new RoleCreatedDomainEvent(role.Id, tenantId, departmentId, name.Value, correlationId));
        return Result<Role>.Success(role);
    }

    public Result<Unit> Activate(Guid correlationId)
    {
        if (Status == RoleStatus.Disabled) return Result<Unit>.Failure(AuthorizationErrors.RoleIsDisabled);
        if (Status == RoleStatus.Active) return Result<Unit>.Success(Unit.Value);
        Status = RoleStatus.Active;
        MarkUpdated();
        RaiseDomainEvent(new RoleActivatedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Deactivate(Guid correlationId)
    {
        if (Status == RoleStatus.Disabled) return Result<Unit>.Failure(AuthorizationErrors.RoleIsDisabled);
        if (Status == RoleStatus.Inactive) return Result<Unit>.Success(Unit.Value);
        Status = RoleStatus.Inactive;
        MarkUpdated();
        RaiseDomainEvent(new RoleDeactivatedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Disable(Guid correlationId)
    {
        if (Status == RoleStatus.Disabled) return Result<Unit>.Success(Unit.Value);
        Status = RoleStatus.Disabled;
        MarkUpdated();
        RaiseDomainEvent(new RoleDisabledDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> SetParent(RoleId? parentRoleId, int parentHierarchyDepth, Guid correlationId)
    {
        if (parentRoleId is not null && parentRoleId.Value == Id)
            return Result<Unit>.Failure(AuthorizationErrors.RoleSelfInheritance);

        var newDepth = parentHierarchyDepth + 1;
        if (newDepth > MaxHierarchyDepth)
            return Result<Unit>.Failure(AuthorizationErrors.MaxHierarchyDepthExceeded);

        ParentRoleId = parentRoleId;
        HierarchyDepth = newDepth;
        MarkUpdated();
        RaiseDomainEvent(new RoleParentChangedDomainEvent(Id, TenantId, parentRoleId?.Value, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> EnsureTenantMatch(Guid expectedTenantId)
    {
        if (TenantId != expectedTenantId)
            return Result<Unit>.Failure(AuthorizationErrors.RoleTenantMismatch);

        return Result<Unit>.Success(Unit.Value);
    }
}
