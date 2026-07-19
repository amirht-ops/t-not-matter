using IdentityService.Domain.Aggregates.Session;
using IdentityService.Domain.Repositories;
using IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Persistence.Repositories;

public sealed class SessionRepository(IdentityDbContext dbContext) : ISessionRepository
{
    public Task<Session?> GetByIdAsync(Guid tenantId, Guid sessionId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Sessions.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(session => session.TenantId == tenantId && session.Id == sessionId, cancellationToken);
    }

    public Task<Session?> GetByRefreshTokenAsync(string refreshToken, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (!RefreshToken.TryParse(refreshToken, out _, out var sessionId, out var randomPart))
            return Task.FromResult<Session?>(null);

        var query = dbContext.Sessions.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(
            session => session.Id == sessionId && session.RefreshTokenHash == RefreshToken.Hash(randomPart),
            cancellationToken);
    }

    public Task<Session?> GetByPreviousRefreshTokenAsync(string refreshToken, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (!RefreshToken.TryParse(refreshToken, out _, out var sessionId, out var randomPart))
            return Task.FromResult<Session?>(null);

        var query = dbContext.Sessions.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(
            session => session.Id == sessionId && session.PreviousRefreshTokenHash == RefreshToken.Hash(randomPart),
            cancellationToken);
    }

    public Task<IReadOnlyList<Session>> GetByUserIdAsync(Guid tenantId, Guid userId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        return ListAsync(tenantId, userId, trackChanges, false, cancellationToken);
    }

    public Task<IReadOnlyList<Session>> GetByTenantIdAsync(Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        return ListAsync(tenantId, null, trackChanges, true, cancellationToken);
    }

    private async Task<IReadOnlyList<Session>> ListAsync(Guid tenantId, Guid? userId, bool trackChanges, bool ignoreFilters, CancellationToken cancellationToken)
    {
        var query = dbContext.Sessions.AsQueryable();
        if (ignoreFilters) query = query.IgnoreQueryFilters();
        if (!trackChanges) query = query.AsNoTracking();
        if (userId.HasValue)
            query = query.Where(session => session.UserId == userId.Value);
        return await query.Where(session => session.TenantId == tenantId).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Session session, CancellationToken cancellationToken)
    {
        await dbContext.Sessions.AddAsync(session, cancellationToken);
    }

    public Task UpdateAsync(Session session, CancellationToken cancellationToken)
    {
        if (dbContext.Entry(session).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            dbContext.Sessions.Update(session);
        }
        return Task.CompletedTask;
    }
}
