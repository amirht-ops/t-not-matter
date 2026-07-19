using AuthorizationService.Domain.Aggregates.RoleAssignment;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Infrastructure.Persistence.Repositories;

public sealed class RoleAssignmentRepository(AuthorizationDbContext dbContext) : IRoleAssignmentRepository
{
    public async Task<IReadOnlyCollection<RoleAssignment>> GetActiveForSubjectAsync(Guid tenantId, SubjectId subjectId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.RoleAssignments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return await query.Where(assignment => assignment.TenantId == tenantId && assignment.SubjectId == subjectId && assignment.RevokedAtUtc == null).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> GetActiveRoleIdsIncludingInheritedAsync(Guid tenantId, SubjectId subjectId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.RoleAssignments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        var directRoleIds = await query.Where(assignment => assignment.TenantId == tenantId && assignment.SubjectId == subjectId && assignment.RevokedAtUtc == null).Select(assignment => assignment.RoleId).ToArrayAsync(cancellationToken);
        var allRoles = await dbContext.Roles.AsNoTracking().Where(role => role.TenantId == tenantId).Select(role => new { role.Id, role.ParentRoleId }).ToArrayAsync(cancellationToken);
        var result = new HashSet<RoleId>(directRoleIds);
        var frontier = new Queue<RoleId>(directRoleIds);
        while (frontier.Count > 0)
        {
            var roleId = frontier.Dequeue();
            var parent = allRoles.FirstOrDefault(role => role.Id == roleId.Value)?.ParentRoleId;
            if (parent is RoleId parentRoleId && result.Add(parentRoleId)) frontier.Enqueue(parentRoleId);
        }
        return result.Select(r => r.Value).ToArray();
    }

    public async Task<IReadOnlyCollection<string>> GetActiveRoleNamesForSubjectAsync(Guid tenantId, SubjectId subjectId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.RoleAssignments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        var roleIds = await query.Where(assignment => assignment.TenantId == tenantId && assignment.SubjectId == subjectId && assignment.RevokedAtUtc == null).Select(assignment => assignment.RoleId).ToArrayAsync(cancellationToken);
        var roleGuidIds = roleIds.Select(r => r.Value).ToArray();
        return await dbContext.Roles.AsNoTracking().Where(role => role.TenantId == tenantId && roleGuidIds.Contains(role.Id)).Select(role => role.Name.Value).Distinct().ToArrayAsync(cancellationToken);
    }
    public Task<RoleAssignment?> GetActiveAsync(Guid tenantId, SubjectId subjectId, Guid roleId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.RoleAssignments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        var roleIdVO = RoleId.From(roleId);
        return query.FirstOrDefaultAsync(assignment => assignment.TenantId == tenantId && assignment.SubjectId == subjectId && assignment.RoleId == roleIdVO && assignment.RevokedAtUtc == null, cancellationToken);
    }
    public async Task<IReadOnlyCollection<SubjectId>> GetSubjectIdsByRoleAsync(Guid tenantId, Guid roleId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var roleIdVO = RoleId.From(roleId);
        var query = dbContext.RoleAssignments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return await query.Where(a => a.TenantId == tenantId && a.RoleId == roleIdVO && a.RevokedAtUtc == null).Select(a => a.SubjectId).Distinct().ToArrayAsync(cancellationToken);
    }
    public async Task AddAsync(RoleAssignment assignment, CancellationToken cancellationToken) => await dbContext.RoleAssignments.AddAsync(assignment, cancellationToken);
    public Task UpdateAsync(RoleAssignment assignment, CancellationToken cancellationToken) { return Task.CompletedTask; }
}
