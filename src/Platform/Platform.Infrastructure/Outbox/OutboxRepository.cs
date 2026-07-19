using Microsoft.EntityFrameworkCore;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Infrastructure.Outbox;

namespace Platform.Infrastructure.Outbox;

public class OutboxRepository<TDbContext> : IPlatformOutboxRepository<TDbContext> where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    public OutboxRepository(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<OutboxMessage>> ClaimPendingBatchAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var leaseCutoff = DateTimeOffset.UtcNow.Add(leaseDuration);
        var (schema, tableName) = GetTableInfo();

        var sql = $$"""
            UPDATE "{{schema}}"."{{tableName}}"
            SET "NextRetryAt" = {0}
            WHERE "Id" IN (
                SELECT "Id"
                FROM "{{schema}}"."{{tableName}}"
                WHERE "ProcessedAt" IS NULL
                  AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= NOW())
                ORDER BY "CreatedAt"
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
            )
            """;

        await _dbContext.Database.ExecuteSqlRawAsync(sql, [leaseCutoff, batchSize], cancellationToken);

        return await _dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null
                && m.NextRetryAt > DateTimeOffset.UtcNow
                && m.NextRetryAt <= leaseCutoff)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await _dbContext.Set<OutboxMessage>().FindAsync([messageId], cancellationToken: cancellationToken);
        if (message != null)
        {
            message.MarkProcessed();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken)
    {
        var message = await _dbContext.Set<OutboxMessage>().FindAsync([messageId], cancellationToken: cancellationToken);
        if (message != null)
        {
            message.MarkFailed(error);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private (string Schema, string TableName) GetTableInfo()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(OutboxMessage));
        var schema = entityType?.GetSchema() ?? "public";
        var tableName = entityType?.GetTableName() ?? "outbox_messages";
        return (schema, tableName);
    }
}
