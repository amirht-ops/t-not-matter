using IdentityService.Domain.Aggregates;
using IdentityService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Persistence.Repositories;

public sealed class AccessRiskRepository(IdentityDbContext dbContext) : IAccessRiskRepository
{
    public async Task<AccessRisk?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: login may resolve before TenantId is available in request context.
        // AccessRisk.Id IS the UserId — query by the primary key.
        return await dbContext.Set<AccessRisk>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == userId, cancellationToken);
    }

    public async Task AddAsync(AccessRisk accessRisk, CancellationToken cancellationToken)
    {
        await dbContext.Set<AccessRisk>().AddAsync(accessRisk, cancellationToken);
    }

    public Task UpdateAsync(AccessRisk accessRisk, CancellationToken cancellationToken)
    {
        // No-op: EF Core change tracking detects mutations to tracked entities.
        // AccessRisk is always fetched with tracking (GetByUserIdAsync returns tracked entities
        // when the caller needs to mutate). SaveChangesAsync persists the changes.
        return Task.CompletedTask;
    }
}
