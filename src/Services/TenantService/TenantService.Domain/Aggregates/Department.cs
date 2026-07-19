using SharedKernel.Domain.Primitives;
using SharedKernel.Results;
using TenantService.Domain.Enums;
using TenantService.Domain.Errors;
using TenantService.Domain.Events;
using TenantService.Domain.ValueObjects;

namespace TenantService.Domain.Aggregates;

public sealed class Department : AggregateRoot
{
    private Department() { }

    private Department(Guid id, Guid tenantId, DepartmentName name, string? description)
        : base(id, tenantId)
    {
        Name = name;
        Description = description;
        Status = DepartmentStatus.Active;
    }

    public DepartmentName Name { get; private set; }
    public string? Description { get; private set; }
    public DepartmentStatus Status { get; private set; }

    public bool IsActive => Status == DepartmentStatus.Active;

    public static Result<Department> Create(Guid tenantId, string name, string? description, Guid correlationId)
    {
        if (tenantId == Guid.Empty)
        {
            return Result<Department>.Failure(TenantErrors.TenantNotFound);
        }

        var nameResult = DepartmentName.Create(name);
        if (nameResult.IsFailure)
        {
            return Result<Department>.Failure(nameResult.Error);
        }

        var department = new Department(Guid.NewGuid(), tenantId, nameResult.Value!, description?.Trim());
        department.RaiseDomainEvent(new DepartmentCreatedEvent(tenantId, department.Id, nameResult.Value!.Value,
            correlationId));
        return Result<Department>.Success(department);
    }

    public Result<Unit> Activate(Guid correlationId)
    {
        if (Status == DepartmentStatus.Active)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var previousStatus = Status.ToString();
        Status = DepartmentStatus.Active;
        MarkUpdated();
        RaiseDomainEvent(new DepartmentStatusChangedEvent(TenantId, Id, previousStatus, Status.ToString(),
            correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Deactivate(Guid correlationId)
    {
        if (Status == DepartmentStatus.Inactive)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var previousStatus = Status.ToString();
        Status = DepartmentStatus.Inactive;
        MarkUpdated();
        RaiseDomainEvent(new DepartmentStatusChangedEvent(TenantId, Id, previousStatus, Status.ToString(),
            correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> UpdateName(string newName, Guid correlationId)
    {
        var nameResult = DepartmentName.Create(newName);
        if (nameResult.IsFailure)
        {
            return Result<Unit>.Failure(nameResult.Error);
        }

        if (Name == nameResult.Value)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var previousName = Name.Value;
        Name = nameResult.Value!;
        MarkUpdated();
        RaiseDomainEvent(new DepartmentNameUpdatedEvent(TenantId, Id, previousName, Name.Value, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> UpdateDescription(string? newDescription, Guid correlationId)
    {
        var trimmed = newDescription?.Trim();
        if (Description == trimmed)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var previousDescription = Description;
        Description = trimmed;
        MarkUpdated();
        RaiseDomainEvent(new DepartmentDescriptionUpdatedEvent(TenantId, Id, previousDescription, trimmed, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> SoftDelete(Guid correlationId)
    {
        // Flip the base soft-delete flag (sets IsDeleted = true and bumps Version) so the
        // global query filter hides the row. Previously this only raised the event, leaving
        // the department readable after a DELETE.
        base.SoftDelete();
        RaiseDomainEvent(new DepartmentSoftDeletedEvent(TenantId, Id, Name.Value, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

}
