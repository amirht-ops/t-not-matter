using Microsoft.EntityFrameworkCore;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using PolicyService.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Repositories;

public sealed class UsageLedgerRepository(PolicyDbContext dbContext) : IUsageLedgerRepository
{
    public Task<UsageLedger?> GetByIdAsync(Guid tenantId, UsageLedgerId usageLedgerId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        trackChanges
            ? dbContext.UsageLedgers.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == usageLedgerId.Value, cancellationToken)
            : dbContext.UsageLedgers.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == usageLedgerId.Value, cancellationToken);

    public Task<UsageLedger?> GetByConsumerIdAsync(Guid tenantId, ConsumerId consumerId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        trackChanges
            ? dbContext.UsageLedgers.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.ConsumerId == consumerId, cancellationToken)
            : dbContext.UsageLedgers.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == tenantId && l.ConsumerId == consumerId, cancellationToken);

    public async Task<UsageLedger?> GetOrCreateAsync(Guid tenantId, ConsumerId consumerId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.UsageLedgers.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.ConsumerId == consumerId, cancellationToken);
        if (existing is not null)
            return existing;

        var created = UsageLedger.Create(tenantId, consumerId);
        if (created.IsFailure)
            return null;

        dbContext.UsageLedgers.Add(created.Value!);
        return created.Value!;
    }

    public Task UpdateAsync(UsageLedger usageLedger, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Acquires a PostgreSQL transaction-scoped advisory lock for the given (tenant, consumer) pair.
    /// Concurrent RecordConsumption operations for the same consumer will block here until the
    /// current transaction commits or rolls back, eliminating optimistic-concurrency (Version)
    /// collisions on the usage-ledger and usage-counter rows.
    ///
    /// The lock key is a deterministic 64-bit integer derived from the XOR of the tenant and
    /// consumer GUIDs. It is always acquired before any ledger read, inside the ambient transaction
    /// started by UnitOfWorkBehavior.
    /// </summary>
    public async Task AcquireConsumerLockAsync(Guid tenantId, ConsumerId consumerId, CancellationToken cancellationToken = default)
    {
        var lockKey = ComputeLockKey(tenantId, consumerId.Value);
        // pg_advisory_xact_lock blocks until the lock is available, then holds it for the
        // lifetime of the current transaction. No explicit release is needed.
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [lockKey], cancellationToken);
    }

    /// <summary>
    /// Produces a stable 64-bit advisory-lock key from two GUIDs. Uses XOR across all 16 bytes
    /// of each GUID so the high and low halves both contribute to uniqueness.
    /// </summary>
    private static long ComputeLockKey(Guid tenantId, Guid consumerId)
    {
        var tb = tenantId.ToByteArray();
        var cb = consumerId.ToByteArray();
        var lo = BitConverter.ToInt64(tb, 0) ^ BitConverter.ToInt64(cb, 0);
        var hi = BitConverter.ToInt64(tb, 8) ^ BitConverter.ToInt64(cb, 8);
        return lo ^ hi;
    }
}
