using IdentityService.Domain.Aggregates.Session;

namespace IdentityService.Domain.Repositories;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid tenantId, Guid sessionId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<Session?> GetByRefreshTokenAsync(string refreshToken, bool trackChanges = false, CancellationToken cancellationToken = default);

    Task<Session?> GetByPreviousRefreshTokenAsync(string refreshToken, bool trackChanges = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetByUserIdAsync(Guid tenantId, Guid userId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Session>> GetByTenantIdAsync(Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task AddAsync(Session session, CancellationToken cancellationToken);
    Task UpdateAsync(Session session, CancellationToken cancellationToken);
}