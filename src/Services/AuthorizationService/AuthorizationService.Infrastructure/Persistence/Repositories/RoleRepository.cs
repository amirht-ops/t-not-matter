using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Infrastructure.Persistence.Repositories;

public sealed class RoleRepository(AuthorizationDbContext dbContext) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid tenantId, Guid roleId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Roles.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(role => role.TenantId == tenantId && role.Id == roleId, cancellationToken);
    }
    public Task<Role?> GetByNameAsync(Guid tenantId, Guid departmentId, RoleName name, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Roles.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(role => role.TenantId == tenantId && role.DepartmentId == departmentId && role.Name == name, cancellationToken);
    }
    public Task<bool> ExistsByNameAsync(Guid tenantId, Guid departmentId, RoleName name, CancellationToken cancellationToken) => dbContext.Roles.AsNoTracking().AnyAsync(role => role.TenantId == tenantId && role.DepartmentId == departmentId && role.Name == name, cancellationToken);
    public async Task AddAsync(Role role, CancellationToken cancellationToken) => await dbContext.Roles.AddAsync(role, cancellationToken);
    public Task UpdateAsync(Role role, CancellationToken cancellationToken) { return Task.CompletedTask; }
}
