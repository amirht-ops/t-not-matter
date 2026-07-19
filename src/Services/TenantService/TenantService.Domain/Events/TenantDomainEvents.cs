using SharedKernel.Domain.Events;

namespace TenantService.Domain.Events;

public abstract record TenantDomainEvent(Guid TenantId, Guid CorrelationId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    public int Version { get; } = 1;
    public Guid? CausationId { get; init; }
    public abstract string EventTypeName { get; }
}

public sealed record TenantCreatedEvent(
    Guid TenantId,
    string Identifier,
    string Name,
    string Slug,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "TenantCreatedV1";
    public string Identifier { get; } = Identifier;
    public string Name { get; } = Name;
    public string Slug { get; } = Slug;
}

public sealed record TenantStatusChangedEvent(
    Guid TenantId,
    string PreviousStatus,
    string NewStatus,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "TenantStatusChangedV1";
    public string PreviousStatus { get; } = PreviousStatus;
    public string NewStatus { get; } = NewStatus;
}

public sealed record TenantPlanUpgradedEvent(
    Guid TenantId,
    string PreviousPlanTier,
    string NewPlanTier,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "TenantPlanUpgradedV1";
    public string PreviousPlanTier { get; } = PreviousPlanTier;
    public string NewPlanTier { get; } = NewPlanTier;
}

public sealed record TenantNameUpdatedEvent(
    Guid TenantId,
    string PreviousName,
    string NewName,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "TenantNameUpdatedV1";
    public string PreviousName { get; } = PreviousName;
    public string NewName { get; } = NewName;
}

public sealed record DepartmentCreatedEvent(
    Guid TenantId,
    Guid DepartmentId,
    string Name,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "DepartmentCreatedV1";
    public Guid DepartmentId { get; } = DepartmentId;
    public string Name { get; } = Name;
}

public sealed record DepartmentStatusChangedEvent(
    Guid TenantId,
    Guid DepartmentId,
    string PreviousStatus,
    string NewStatus,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "DepartmentStatusChangedV1";
    public Guid DepartmentId { get; } = DepartmentId;
    public string PreviousStatus { get; } = PreviousStatus;
    public string NewStatus { get; } = NewStatus;
}

public sealed record DepartmentNameUpdatedEvent(
    Guid TenantId,
    Guid DepartmentId,
    string PreviousName,
    string NewName,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "DepartmentNameUpdatedV1";
    public Guid DepartmentId { get; } = DepartmentId;
    public string PreviousName { get; } = PreviousName;
    public string NewName { get; } = NewName;
}

public sealed record DepartmentDescriptionUpdatedEvent(
    Guid TenantId,
    Guid DepartmentId,
    string? PreviousDescription,
    string? NewDescription,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "DepartmentDescriptionUpdatedV1";
    public Guid DepartmentId { get; } = DepartmentId;
    public string? PreviousDescription { get; } = PreviousDescription;
    public string? NewDescription { get; } = NewDescription;
}

public sealed record DepartmentSoftDeletedEvent(
    Guid TenantId,
    Guid DepartmentId,
    string Name,
    Guid CorrelationId)
    : TenantDomainEvent(TenantId, CorrelationId)
{
    public override string EventTypeName => "DepartmentSoftDeletedV1";
    public Guid DepartmentId { get; } = DepartmentId;
    public string Name { get; } = Name;
}
