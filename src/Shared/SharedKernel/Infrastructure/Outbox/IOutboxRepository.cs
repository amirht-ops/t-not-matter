namespace SharedKernel.Infrastructure.Outbox;

public interface IOutboxRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OutboxMessage>> ClaimPendingBatchAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken);
}
