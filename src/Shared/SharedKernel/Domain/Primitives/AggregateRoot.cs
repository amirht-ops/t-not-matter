using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Primitives;

public abstract class AggregateRoot : Entity
{
    protected AggregateRoot() : base() { }

    protected AggregateRoot(Guid id, Guid tenantId) : base(id, tenantId)
    {
    }

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}