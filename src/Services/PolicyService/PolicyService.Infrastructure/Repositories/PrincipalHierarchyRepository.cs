using Microsoft.EntityFrameworkCore;
using PolicyService.Domain.Aggregates.PrincipalHierarchy;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Repositories;

public sealed class PrincipalHierarchyRepository(PolicyDbContext dbContext) : IPrincipalHierarchyRepository
{
    public Task<PrincipalNode?> GetNodeAsync(Guid tenantId, Guid nodeId, CancellationToken cancellationToken = default) =>
        dbContext.PrincipalNodes.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.NodeId == nodeId && !n.IsDeleted, cancellationToken);

    public async Task<PrincipalNode> GetOrCreateNodeAsync(Guid tenantId, Guid nodeId, PrincipalKind nodeType, CancellationToken cancellationToken = default)
    {
        var existing = await GetNodeAsync(tenantId, nodeId, cancellationToken);
        if (existing is not null)
            return existing;

        var node = PrincipalNode.Create(tenantId, nodeId, nodeType);
        dbContext.PrincipalNodes.Add(node);
        return node;
    }

    public Task<PrincipalEdge?> GetEdgeAsync(Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default) =>
        dbContext.PrincipalEdges.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId && e.RoleId == roleId && !e.IsDeleted, cancellationToken);

    public async Task<PrincipalEdge> GetOrCreateEdgeAsync(Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var existing = await GetEdgeAsync(tenantId, userId, roleId, cancellationToken);
        if (existing is not null)
            return existing;

        var edge = PrincipalEdge.Create(tenantId, userId, roleId);
        dbContext.PrincipalEdges.Add(edge);
        return edge;
    }

    public async Task<IReadOnlyCollection<PrincipalEdge>> ListEdgesByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var edges = await dbContext.PrincipalEdges
            .Where(e => e.TenantId == tenantId && e.UserId == userId && !e.IsDeleted)
            .ToListAsync(cancellationToken);

        return edges;
    }

    public Task RemoveEdgeAsync(PrincipalEdge edge, CancellationToken cancellationToken)
    {
        edge.Revoke();
        return Task.CompletedTask;
    }
}
