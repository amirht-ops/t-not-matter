using Microsoft.EntityFrameworkCore;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using PolicyService.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Repositories;

public sealed class QuotaPolicyRepository(PolicyDbContext dbContext) : IQuotaPolicyRepository
{
    public Task<QuotaPolicy?> GetByIdAsync(Guid tenantId, QuotaPolicyId quotaPolicyId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        trackChanges
            ? dbContext.QuotaPolicies.FirstOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quotaPolicyId.Value, cancellationToken)
            : dbContext.QuotaPolicies.AsNoTracking().FirstOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quotaPolicyId.Value, cancellationToken);

    public Task<QuotaPolicy?> GetByScopeAsync(Guid tenantId, SubscriptionScope scope, CancellationToken cancellationToken = default) =>
        dbContext.QuotaPolicies.AsNoTracking().FirstOrDefaultAsync(q => q.TenantId == tenantId && q.Scope == scope, cancellationToken);

    public async Task<IReadOnlyCollection<QuotaPolicy>> ListByScopeAsync(Guid tenantId, SubscriptionScope scope, CancellationToken cancellationToken = default)
    {
        var policies = await dbContext.QuotaPolicies.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.Scope == scope)
            .ToListAsync(cancellationToken);
        return policies.AsReadOnly();
    }

    public async Task AddAsync(QuotaPolicy quotaPolicy, CancellationToken cancellationToken) => await dbContext.QuotaPolicies.AddAsync(quotaPolicy, cancellationToken);
    public Task UpdateAsync(QuotaPolicy quotaPolicy, CancellationToken cancellationToken) => Task.CompletedTask;
}
