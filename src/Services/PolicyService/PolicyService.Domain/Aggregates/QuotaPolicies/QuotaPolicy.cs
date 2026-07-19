using PolicyService.Domain.Enums;
using PolicyService.Domain.Events;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.Aggregates.QuotaPolicies;

/// <summary>
/// The quota definition (per-window limits), owned at some level of the org hierarchy.
/// A separate aggregate from usage/debt (different lifecycle, ADR-018 §7). <see cref="Quota"/> is
/// a Value Object inside this aggregate.
/// </summary>
public sealed class QuotaPolicy : AggregateRoot
{
    private QuotaPolicy() { }

    private QuotaPolicy(Guid id, Guid tenantId, SubscriptionScope scope, Quota quota, bool enabled)
        : base(id, tenantId)
    {
        Scope = scope;
        Quota = quota;
        IsEnabled = enabled;
    }

    public SubscriptionScope Scope { get; private set; } = null!;
    public Quota Quota { get; private set; } = null!;
    public bool IsEnabled { get; private set; }

    public static Result<QuotaPolicy> Create(Guid tenantId, SubscriptionScope scope, Quota quota, Guid correlationId)
    {
        if (tenantId == Guid.Empty)
            return Result<QuotaPolicy>.Failure(PolicyErrors.TenantMismatch);
        if (scope is null)
            return Result<QuotaPolicy>.Failure(PolicyErrors.QuotaPolicyScopeRequired);
        if (quota is null)
            return Result<QuotaPolicy>.Failure(PolicyErrors.QuotaLimitInvalid);

        var policy = new QuotaPolicy(Guid.NewGuid(), tenantId, scope, quota, true);
        policy.RaiseDomainEvent(new QuotaPolicyDefinedDomainEvent(QuotaPolicyId.From(policy.Id), scope, quota, tenantId, correlationId));
        return Result<QuotaPolicy>.Success(policy);
    }

    public Result<Unit> Amend(Quota quota, Guid correlationId)
    {
        if (quota is null)
            return Result<Unit>.Failure(PolicyErrors.QuotaLimitInvalid);

        Quota = quota;
        MarkUpdated();
        RaiseDomainEvent(new QuotaPolicyAmendedDomainEvent(QuotaPolicyId.From(Id), quota, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Remove(Guid correlationId)
    {
        if (IsDeleted)
            return Result<Unit>.Success(Unit.Value);

        SoftDelete();
        RaiseDomainEvent(new QuotaPolicyRemovedDomainEvent(QuotaPolicyId.From(Id), TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public QuotaPolicyId GetQuotaPolicyId() => QuotaPolicyId.From(Id);
}
