using IdentityService.Application.Common.Abstractions;
using IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel.Infrastructure.Outbox;

namespace IdentityService.Infrastructure.Outbox;

public sealed class OutboxRepository(IdentityDbContext dbContext) : IOutboxRepository
{
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);

    public async Task<IReadOnlyCollection<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken) =>
        await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.ProcessedAt == null && (message.NextRetryAt == null || message.NextRetryAt <= DateTimeOffset.UtcNow))
            .OrderBy(message => message.CreatedAt)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<OutboxMessage>> ClaimPendingBatchAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.Add(leaseDuration);
        const string sql = """
                           WITH cte AS (
                               SELECT "Id"
                               FROM "Identity"."identity_outbox"
                               WHERE "ProcessedAt" IS NULL AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= @now)
                               ORDER BY "CreatedAt"
                               LIMIT @batchSize
                               FOR UPDATE SKIP LOCKED
                           )
                           UPDATE "Identity"."identity_outbox" AS o
                           SET "NextRetryAt" = @leaseUntil
                           FROM cte
                           WHERE o."Id" = cte."Id"
                           RETURNING o."Id", o."TenantId", o."CorrelationId", o."CausationId", o."EventType", o."Payload", o."Version", o."CreatedAt", o."ProcessedAt", o."NextRetryAt", o."Error", o."RetryCount";
                           """;

        var claimed = await dbContext.OutboxMessages
            .FromSqlRaw(
                sql,
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("batchSize", batchSize),
                new NpgsqlParameter("leaseUntil", leaseUntil))
            .ToArrayAsync(cancellationToken);

        return claimed;
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await dbContext.OutboxMessages.FirstOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.MarkProcessed();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken)
    {
        var message = await dbContext.OutboxMessages.FirstOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.MarkFailed(error);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class IdentityUnitOfWork(IdentityDbContext dbContext) : IIdentityUnitOfWork
{
    private IDbContextTransaction? transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            return;
        }

        transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
        => await dbContext.SaveChangesAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public void ClearChangeTracker() => dbContext.ChangeTracker.Clear();
}
