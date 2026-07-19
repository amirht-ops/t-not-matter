using Microsoft.EntityFrameworkCore;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using PolicyService.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Repositories;

public sealed class PolicyRepository(PolicyDbContext dbContext) : IPolicyRepository
{
    public Task<Policy?> GetByIdAsync(Guid tenantId, PolicyId policyId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        trackChanges
            ? dbContext.Policies.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == policyId.Value, cancellationToken)
            : dbContext.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == policyId.Value, cancellationToken);

    public async Task<IReadOnlyCollection<Policy>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var policies = await dbContext.Policies.AsNoTracking().Where(p => p.TenantId == tenantId).ToListAsync(cancellationToken);
        return policies.AsReadOnly();
    }

    public async Task AddAsync(Policy policy, CancellationToken cancellationToken) => await dbContext.Policies.AddAsync(policy, cancellationToken);
    public Task UpdateAsync(Policy policy, CancellationToken cancellationToken) => Task.CompletedTask;
}
