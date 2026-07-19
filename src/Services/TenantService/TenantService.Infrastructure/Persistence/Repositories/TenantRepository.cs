using Microsoft.EntityFrameworkCore;
using TenantService.Domain.Aggregates;
using TenantService.Domain.Repositories;
using SharedKernel.Domain.ValueObjects;
using TenantCode = TenantService.Domain.ValueObjects.TenantCode;

namespace TenantService.Infrastructure.Persistence.Repositories;

public sealed class TenantRepository(TenantDbContext dbContext) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Tenants.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public Task<Tenant?> GetByCodeAsync(TenantCode code, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Tenants.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(t => t.Code == code, cancellationToken);
    }

    public Task<Tenant?> GetBySlugAsync(TenantSlug slug, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Tenants.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);
    }

    public async Task<bool> ExistsByCodeAsync(TenantCode code, CancellationToken cancellationToken)
    {
        return await dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Code == code, cancellationToken);
    }

    public async Task<bool> ExistsBySlugAsync(TenantSlug slug, CancellationToken cancellationToken)
    {
        return await dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Slug == slug, cancellationToken);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await dbContext.Tenants.AddAsync(tenant, cancellationToken);
    }

    public Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
