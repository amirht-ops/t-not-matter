using TenantService.Domain.Aggregates;

namespace TenantService.Domain.Repositories;

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(Guid tenantId, Guid id, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<Department?> GetByNameAsync(Guid tenantId, string name, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Department>> GetByTenantIdAsync(Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);
    Task AddAsync(Department department, CancellationToken cancellationToken = default);
    Task UpdateAsync(Department department, CancellationToken cancellationToken = default);
}