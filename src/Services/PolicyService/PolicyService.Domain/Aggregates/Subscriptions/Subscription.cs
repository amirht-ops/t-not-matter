using PolicyService.Domain.Enums;
using PolicyService.Domain.Events;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.Aggregates.Subscriptions;

/// <summary>
/// Binds a <see cref="Policy"/> to a scope (Tenant / Role / User) with precedence.
/// Resolved User → Role → Tenant (first/highest precedence wins). Separate aggregate from Policy.
/// </summary>
public sealed class Subscription : AggregateRoot
{
    private Subscription() { }

    private Subscription(Guid id, Guid tenantId, PolicyId policyId, SubscriptionScope scope, SubscriptionStatus status, DateTimeOffset effectiveFrom)
        : base(id, tenantId)
    {
        PolicyId = policyId;
        Scope = scope;
        Status = status;
        EffectiveFrom = effectiveFrom;
    }

    public PolicyId PolicyId { get; private set; } = null!;
    public SubscriptionScope Scope { get; private set; } = null!;
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }

    public static Result<Subscription> Create(Guid tenantId, PolicyId policyId, SubscriptionScope scope, Guid correlationId)
    {
        if (tenantId == Guid.Empty)
            return Result<Subscription>.Failure(PolicyErrors.TenantMismatch);
        if (policyId is null)
            return Result<Subscription>.Failure(PolicyErrors.PolicyNotFound);
        if (scope is null)
            return Result<Subscription>.Failure(PolicyErrors.SubscriptionScopeRequired);

        var subscription = new Subscription(Guid.NewGuid(), tenantId, policyId, scope, SubscriptionStatus.Assigned, DateTimeOffset.UtcNow);
        subscription.RaiseDomainEvent(new SubscriptionAssignedDomainEvent(SubscriptionId.From(subscription.Id), policyId, scope, tenantId, correlationId));
        return Result<Subscription>.Success(subscription);
    }

    public Result<Unit> Activate(Guid correlationId)
    {
        if (Status is SubscriptionStatus.Revoked or SubscriptionStatus.Superseded)
            return Result<Unit>.Failure(PolicyErrors.InvalidSubscriptionLifecycle);
        if (Status == SubscriptionStatus.Active)
            return Result<Unit>.Success(Unit.Value);

        Status = SubscriptionStatus.Active;
        MarkUpdated();
        RaiseDomainEvent(new SubscriptionActivatedDomainEvent(SubscriptionId.From(Id), TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Revoke(Guid correlationId)
    {
        if (Status == SubscriptionStatus.Revoked)
            return Result<Unit>.Success(Unit.Value);

        Status = SubscriptionStatus.Revoked;
        MarkUpdated();
        RaiseDomainEvent(new SubscriptionRevokedDomainEvent(SubscriptionId.From(Id), TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Supersede(Guid correlationId)
    {
        if (Status == SubscriptionStatus.Superseded)
            return Result<Unit>.Success(Unit.Value);

        Status = SubscriptionStatus.Superseded;
        MarkUpdated();
        RaiseDomainEvent(new SubscriptionSupersededDomainEvent(SubscriptionId.From(Id), TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public SubscriptionId GetSubscriptionId() => SubscriptionId.From(Id);
}
