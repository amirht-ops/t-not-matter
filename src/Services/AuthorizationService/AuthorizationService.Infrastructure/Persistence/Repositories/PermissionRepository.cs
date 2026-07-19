using AuthorizationService.Domain.Aggregates.Permission;
using AuthorizationService.Domain.Aggregates.PermissionGrant;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Infrastructure.Persistence.Repositories;

public sealed class PermissionRepository(AuthorizationDbContext dbContext) : IPermissionRepository
{
    public Task<Permission?> GetByIdAsync(Guid tenantId, Guid permissionId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Permissions.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(permission => permission.TenantId == tenantId && permission.Id == permissionId, cancellationToken);
    }
    public Task<Permission?> GetByKeyAsync(Guid tenantId, PermissionKey key, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Permissions.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(permission => permission.TenantId == tenantId && permission.Key == key, cancellationToken);
    }
    public Task<bool> ExistsByKeyAsync(Guid tenantId, PermissionKey key, CancellationToken cancellationToken) => dbContext.Permissions.AsNoTracking().AnyAsync(permission => permission.TenantId == tenantId && permission.Key == key, cancellationToken);
    public async Task<IReadOnlyCollection<string>> GetActivePermissionKeysForRolesAsync(Guid tenantId, IReadOnlyCollection<Guid> roleIds, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var grantQuery = dbContext.PermissionGrants.AsQueryable();
        if (!trackChanges) grantQuery = grantQuery.AsNoTracking();
        var roleIdVOs = roleIds.Select(RoleId.From).ToArray();
        var grantPermissionIds = await grantQuery
            .Where(pg => pg.TenantId == tenantId && roleIdVOs.Contains(pg.RoleId) && pg.RevokedAtUtc == null)
            .Select(pg => pg.PermissionId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var permQuery = dbContext.Permissions.AsQueryable();
        if (!trackChanges) permQuery = permQuery.AsNoTracking();
        var grantPermissionGuidIds = grantPermissionIds.Select(p => p.Value).ToArray();
        return await permQuery
            .Where(p => p.TenantId == tenantId && grantPermissionGuidIds.Contains(p.Id) && p.Lifecycle == PermissionLifecycle.Published)
            .Select(p => p.Key.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
    }
    public async Task AddAsync(Permission permission, CancellationToken cancellationToken) => await dbContext.Permissions.AddAsync(permission, cancellationToken);
    public async Task AddGrantAsync(PermissionGrant grant, CancellationToken cancellationToken) => await dbContext.PermissionGrants.AddAsync(grant, cancellationToken);
    public async Task<PermissionGrant?> GetActiveGrantAsync(Guid tenantId, Guid roleId, Guid permissionId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.PermissionGrants.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        var roleIdVO = RoleId.From(roleId);
        var permissionIdVO = PermissionId.From(permissionId);
        return await query.FirstOrDefaultAsync(grant => grant.TenantId == tenantId && grant.RoleId == roleIdVO && grant.PermissionId == permissionIdVO && grant.RevokedAtUtc == null, cancellationToken);
    }
    public Task UpdateGrantAsync(PermissionGrant grant, CancellationToken cancellationToken)
    {
        dbContext.PermissionGrants.Update(grant);
        return Task.CompletedTask;
    }
}
