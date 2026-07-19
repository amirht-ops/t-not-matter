using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Domain.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid tenantId, Guid roleId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<Role?> GetByNameAsync(Guid tenantId, Guid departmentId, RoleName name, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(Guid tenantId, Guid departmentId, RoleName name, CancellationToken cancellationToken);
    Task AddAsync(Role role, CancellationToken cancellationToken);
    Task UpdateAsync(Role role, CancellationToken cancellationToken);
}
