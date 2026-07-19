using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Domain.Events;

namespace PolicyService.Domain.Events;

/// <summary>
/// Base type for all PolicyService domain events. Carries TenantId + CorrelationId only
/// (ADR-012: never RequestContext). EventTypeName follows "policy.&lt;name&gt;.v1".
/// </summary>
public abstract record PolicyDomainEvent(Guid TenantId, Guid CorrelationId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public Guid? CausationId { get; }
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    public int Version { get; } = 1;
    public abstract string EventTypeName { get; }
}

// ---- Policy aggregate ----

public sealed record PolicyCreatedDomainEvent(PolicyId PolicyId, Guid TenantIdValue, string Name, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.policy-created.v1";
}

public sealed record PolicyPublishedDomainEvent(PolicyId PolicyId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.policy-published.v1";
}

public sealed record PolicyArchivedDomainEvent(PolicyId PolicyId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.policy-archived.v1";
}

public sealed record PolicyConditionChangedDomainEvent(PolicyId PolicyId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.policy-condition-changed.v1";
}

// ---- Subscription aggregate ----

public sealed record SubscriptionAssignedDomainEvent(SubscriptionId SubscriptionId, PolicyId PolicyId, SubscriptionScope Scope, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.subscription-assigned.v1";
}

public sealed record SubscriptionActivatedDomainEvent(SubscriptionId SubscriptionId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.subscription-activated.v1";
}

public sealed record SubscriptionRevokedDomainEvent(SubscriptionId SubscriptionId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.subscription-revoked.v1";
}

public sealed record SubscriptionSupersededDomainEvent(SubscriptionId SubscriptionId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.subscription-superseded.v1";
}

// ---- QuotaPolicy aggregate ----

public sealed record QuotaPolicyDefinedDomainEvent(QuotaPolicyId QuotaPolicyId, SubscriptionScope Scope, Quota Quota, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.quota-policy-defined.v1";
}

public sealed record QuotaPolicyAmendedDomainEvent(QuotaPolicyId QuotaPolicyId, Quota Quota, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.quota-policy-amended.v1";
}

public sealed record QuotaPolicyRemovedDomainEvent(QuotaPolicyId QuotaPolicyId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.quota-policy-removed.v1";
}

// ---- UsageLedger / DebtLedger aggregates ----

public sealed record UsageRecordedDomainEvent(UsageLedgerId UsageLedgerId, ConsumerId ConsumerId, ActionKey ActionKey, QuotaWindow Window, long Count, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.usage-recorded.v1";
}

public sealed record UsageResetDomainEvent(UsageLedgerId UsageLedgerId, ConsumerId ConsumerId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.usage-reset.v1";
}

public sealed record DebtIncurredDomainEvent(DebtLedgerId DebtLedgerId, ConsumerId ConsumerId, ActionKey ActionKey, long Amount, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.debt-incurred.v1";
}

public sealed record DebtRecoveredDomainEvent(DebtLedgerId DebtLedgerId, ConsumerId ConsumerId, ActionKey ActionKey, QuotaWindow Window, long AmountRecovered, long RemainingDebt, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.debt-recovered.v1";
}

public sealed record DebtResetDomainEvent(DebtLedgerId DebtLedgerId, ConsumerId ConsumerId, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.debt-reset.v1";
}

public sealed record QuotaExceededDomainEvent(DebtLedgerId DebtLedgerId, ConsumerId ConsumerId, ActionKey ActionKey, long Excess, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.quota-exceeded.v1";
}

public sealed record OperationAllowedDomainEvent(ConsumerId ConsumerId, ActionKey ActionKey, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.operation-allowed.v1";
}

public sealed record OperationDeniedDomainEvent(ConsumerId ConsumerId, ActionKey ActionKey, string Reason, Guid TenantIdValue, Guid CorrelationIdValue)
    : PolicyDomainEvent(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "policy.operation-denied.v1";
}
