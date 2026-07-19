using Microsoft.Extensions.Logging;
using Platform.Abstractions.Caching;
using Platform.Abstractions.Locking;
using SharedKernel.Caching;
using StackExchange.Redis;

namespace Platform.Caching.Locking;

public sealed class RedisDistributedLockService(
    IRedisConnectionFactory connectionFactory,
    ILogger<RedisDistributedLockService> logger) : IDistributedLockService
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromSeconds(30);

    public async Task<IDistributedLock?> AcquireLockAsync(
        string lockName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        var db = connectionFactory.GetDatabase();
        var lockId = Guid.NewGuid().ToString("N");
        var key = CacheKeyBuilder.DistributedLock(lockName);

        var acquired = await db.StringSetAsync(key, lockId, expiry, When.NotExists);

        if (!acquired)
        {
            logger.LogInformation("Failed to acquire distributed lock {LockName}", lockName);
            return null;
        }

        logger.LogInformation("Acquired distributed lock {LockName} ({LockId})", lockName, lockId);

        return new DistributedLock(lockName, lockId, async (lockInstance, ct) =>
        {
            await ReleaseLockInternalAsync(lockInstance.Name, lockInstance.LockId, ct);
        });
    }

    public async Task ReleaseLockAsync(IDistributedLock distributedLock, CancellationToken cancellationToken = default)
    {
        await ReleaseLockInternalAsync(distributedLock.Name, distributedLock.LockId, cancellationToken);
    }

    private async Task ReleaseLockInternalAsync(string lockName, string lockId, CancellationToken cancellationToken)
    {
        try
        {
            var db = connectionFactory.GetDatabase();
            var key = CacheKeyBuilder.DistributedLock(lockName);

            var script = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                end
                return 0";

            await db.ScriptEvaluateAsync(script, new RedisKey[] { key }, new RedisValue[] { lockId });
            logger.LogInformation("Released distributed lock {LockName} ({LockId})", lockName, lockId);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis lock release failed for {LockName}", lockName);
        }
    }
}
