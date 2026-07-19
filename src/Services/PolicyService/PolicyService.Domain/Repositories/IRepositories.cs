using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.PrincipalHierarchy;
using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;

namespace PolicyService.Domain.Repositories;

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid tenantId, PolicyId policyId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Policy>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(Policy policy, CancellationToken cancellationToken);
    Task UpdateAsync(Policy policy, CancellationToken cancellationToken);
}

public interface ISubscriptionRepository
{
    Task<Subscription?> GetByIdAsync(Guid tenantId, SubscriptionId subscriptionId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<Subscription?> GetByScopeAsync(Guid tenantId, SubscriptionScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Subscription>> ListByPolicyAsync(Guid tenantId, PolicyId policyId, CancellationToken cancellationToken = default);
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken);
    Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken);
}

public interface IQuotaPolicyRepository
{
    Task<QuotaPolicy?> GetByIdAsync(Guid tenantId, QuotaPolicyId quotaPolicyId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<QuotaPolicy?> GetByScopeAsync(Guid tenantId, SubscriptionScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<QuotaPolicy>> ListByScopeAsync(Guid tenantId, SubscriptionScope scope, CancellationToken cancellationToken = default);
    Task AddAsync(QuotaPolicy quotaPolicy, CancellationToken cancellationToken);
    Task UpdateAsync(QuotaPolicy quotaPolicy, CancellationToken cancellationToken);
}

public interface IUsageLedgerRepository
{
    Task<UsageLedger?> GetByIdAsync(Guid tenantId, UsageLedgerId usageLedgerId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<UsageLedger?> GetByConsumerIdAsync(Guid tenantId, ConsumerId consumerId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<UsageLedger?> GetOrCreateAsync(Guid tenantId, ConsumerId consumerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(UsageLedger usageLedger, CancellationToken cancellationToken);

    /// <summary>
    /// Acquires a transaction-scoped PostgreSQL advisory lock keyed on (tenant, consumer) so that
    /// concurrent RecordConsumption operations against the same hot usage-ledger row are serialized
    /// instead of colliding on the optimistic-concurrency (Version) CAS. The lock is released
    /// automatically when the ambient transaction commits or rolls back. Must be called inside the
    /// unit-of-work transaction, before the ledger is read.
    /// </summary>
    Task AcquireConsumerLockAsync(Guid tenantId, ConsumerId consumerId, CancellationToken cancellationToken = default);
}


public interface IDebtLedgerRepository
{
    Task<DebtLedger?> GetByIdAsync(Guid tenantId, DebtLedgerId debtLedgerId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<DebtLedger?> GetByConsumerIdAsync(Guid tenantId, ConsumerId consumerId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<DebtLedger?> GetOrCreateAsync(Guid tenantId, ConsumerId consumerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(DebtLedger debtLedger, CancellationToken cancellationToken);
}

public interface IPrincipalHierarchyRepository
{
    Task<PrincipalNode?> GetNodeAsync(Guid tenantId, Guid nodeId, CancellationToken cancellationToken = default);
    Task<PrincipalNode> GetOrCreateNodeAsync(Guid tenantId, Guid nodeId, PrincipalKind nodeType, CancellationToken cancellationToken = default);
    Task<PrincipalEdge?> GetEdgeAsync(Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task<PrincipalEdge> GetOrCreateEdgeAsync(Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PrincipalEdge>> ListEdgesByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task RemoveEdgeAsync(PrincipalEdge edge, CancellationToken cancellationToken);
}
