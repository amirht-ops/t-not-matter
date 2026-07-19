using Microsoft.Extensions.Logging;
using Platform.Abstractions.Caching;
using Platform.Abstractions.Infrastructure;
using StackExchange.Redis;

namespace Platform.Caching.Idempotency;

public sealed class RedisEventConsumerDeduplicationGuard(
    IRedisConnectionFactory redisConnectionFactory,
    ILogger<RedisEventConsumerDeduplicationGuard> logger) : IEventConsumerDeduplicationGuard
{
    public async Task<bool> TryBeginProcessingAsync(
        string consumerName,
        Guid eventId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var key = $"event-consumer:{consumerName}:{eventId:N}";

        try
        {
            var db = redisConnectionFactory.GetDatabase();
            return await db.StringSetAsync(key, "1", ttl, When.NotExists);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis consumer deduplication failed for key {Key}; processing event fail-open", key);
            return true;
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "Redis consumer deduplication timed out for key {Key}; processing event fail-open", key);
            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
