using SharedKernel.Domain.ValueObjects;
using TenantService.Domain.Aggregates;
using TenantService.Domain.ValueObjects;

namespace TenantService.Domain.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByCodeAsync(TenantCode code, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<Tenant?> GetBySlugAsync(TenantSlug slug, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(TenantCode code, CancellationToken cancellationToken);
    Task<bool> ExistsBySlugAsync(TenantSlug slug, CancellationToken cancellationToken);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);
    Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken);
}