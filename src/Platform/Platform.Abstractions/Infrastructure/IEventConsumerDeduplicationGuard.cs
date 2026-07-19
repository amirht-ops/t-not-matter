namespace Platform.Abstractions.Infrastructure;

public interface IEventConsumerDeduplicationGuard
{
    Task<bool> TryBeginProcessingAsync(
        string consumerName,
        Guid eventId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
}
