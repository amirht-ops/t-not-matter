using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Outbox;

namespace Platform.Abstractions.Infrastructure;

public interface IPlatformOutboxRepository<TDbContext> where TDbContext : DbContext
{
    Task<IReadOnlyCollection<OutboxMessage>> ClaimPendingBatchAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken);
}
