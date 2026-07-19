namespace SharedKernel.Domain.Events;

public interface IDomainEvent
{
    Guid EventId { get; }
    Guid CorrelationId { get; }
    Guid? CausationId { get; }
    Guid TenantId { get; }
    DateTimeOffset OccurredAt { get; }
    int Version { get; }
    string EventTypeName { get; }
}
