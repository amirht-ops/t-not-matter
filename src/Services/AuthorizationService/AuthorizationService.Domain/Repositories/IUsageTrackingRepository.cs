using AuthorizationService.Domain.Aggregates.UsageTracking;
using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Domain.Repositories;

public interface IUsageTrackingRepository
{
    Task<UsageTracking?> GetAsync(Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId, TimeWindow window, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<UsageTracking?> GetByIdAsync(Guid tenantId, Guid trackingId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task AddAsync(UsageTracking tracking, CancellationToken cancellationToken);
    Task UpdateAsync(UsageTracking tracking, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<UsageTracking>> GetExpiredAsync(Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<bool> TryAtomicIncrementAsync(Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId, TimeWindow window, CancellationToken cancellationToken);
}