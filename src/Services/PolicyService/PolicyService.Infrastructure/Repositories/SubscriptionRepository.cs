using Microsoft.EntityFrameworkCore;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using PolicyService.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Repositories;

public sealed class SubscriptionRepository(PolicyDbContext dbContext) : ISubscriptionRepository
{
    public Task<Subscription?> GetByIdAsync(Guid tenantId, SubscriptionId subscriptionId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        trackChanges
            ? dbContext.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == subscriptionId.Value, cancellationToken)
            : dbContext.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == subscriptionId.Value, cancellationToken);

    public Task<Subscription?> GetByScopeAsync(Guid tenantId, SubscriptionScope scope, CancellationToken cancellationToken = default) =>
        dbContext.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Scope == scope, cancellationToken);

    public async Task<IReadOnlyCollection<Subscription>> ListByPolicyAsync(Guid tenantId, PolicyId policyId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await dbContext.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.PolicyId == policyId)
            .ToListAsync(cancellationToken);
        return subscriptions.AsReadOnly();
    }

    public async Task AddAsync(Subscription subscription, CancellationToken cancellationToken) => await dbContext.Subscriptions.AddAsync(subscription, cancellationToken);
    public Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken) => Task.CompletedTask;
}
