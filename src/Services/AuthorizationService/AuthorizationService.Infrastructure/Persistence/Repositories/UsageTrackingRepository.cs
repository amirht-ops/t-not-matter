using AuthorizationService.Domain.Aggregates.UsageTracking;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Infrastructure.Persistence.Repositories;

public sealed class UsageTrackingRepository(AuthorizationDbContext dbContext) : IUsageTrackingRepository
{
    public Task<UsageTracking?> GetAsync(Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId, TimeWindow window, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<UsageTracking>().AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(t => t.TenantId == tenantId
            && t.SubjectId == subjectId
            && t.Action == action
            && t.ResourceType == resourceType
            && t.ResourceId == resourceId
            && t.WindowStart == window.Start
            && t.WindowEnd == window.End
            && !t.IsDeleted, cancellationToken);
    }

    public Task<UsageTracking?> GetByIdAsync(Guid tenantId, Guid trackingId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<UsageTracking>().AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == trackingId && !t.IsDeleted, cancellationToken);
    }

    public async Task AddAsync(UsageTracking tracking, CancellationToken cancellationToken)
    {
        await dbContext.Set<UsageTracking>().AddAsync(tracking, cancellationToken);
    }

    public Task UpdateAsync(UsageTracking tracking, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyCollection<UsageTracking>> GetExpiredAsync(Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var query = dbContext.Set<UsageTracking>().AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return await query.Where(t => t.TenantId == tenantId && t.WindowEnd < now && !t.IsDeleted)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> TryAtomicIncrementAsync(
        Guid tenantId, SubjectId subjectId,
        string action, string resourceType, string resourceId,
        TimeWindow window, CancellationToken cancellationToken)
    {
        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "authz"."UsageTrackings"
            SET "Count" = "Count" + 1,
                "LastAccessedAt" = NOW(),
                "UpdatedAt" = NOW(),
                "Version" = "Version" + 1
            WHERE "TenantId" = {tenantId}
              AND "SubjectId" = {subjectId.Value}
              AND "Action" = {action}
              AND "ResourceType" = {resourceType}
              AND "ResourceId" = {resourceId}
              AND "WindowStart" = {window.Start}
              AND "WindowEnd" = {window.End}
              AND NOT "IsDeleted"
              AND "WindowEnd" > NOW()
            """, cancellationToken);

        return affected > 0;
    }
}
