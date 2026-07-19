using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Factories;

public interface IPolicyFactory
{
    Result<Policy> Create(
        Guid tenantId,
        string name,
        PolicyCondition condition,
        PolicyExpression expression,
        PolicyPriority priority,
        Guid correlationId);
}

public interface ISubscriptionFactory
{
    Result<Subscription> Create(
        Guid tenantId,
        PolicyId policyId,
        SubscriptionScope scope,
        Guid correlationId);
}

public interface IQuotaPolicyFactory
{
    Result<QuotaPolicy> Create(
        Guid tenantId,
        SubscriptionScope scope,
        Quota quota,
        Guid correlationId);
}

public interface IUsageLedgerFactory
{
    Result<UsageLedger> Create(Guid tenantId, ConsumerId consumerId);
}

public interface IDebtLedgerFactory
{
    Result<DebtLedger> Create(Guid tenantId, ConsumerId consumerId);
}
