namespace SharedKernel.Domain.Events;

public interface IDomainEventDIspatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}