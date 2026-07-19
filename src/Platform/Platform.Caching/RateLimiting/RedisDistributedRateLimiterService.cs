using Microsoft.Extensions.Logging;
using Platform.Abstractions.Caching;
using SharedKernel.Caching;
using StackExchange.Redis;

namespace Platform.Caching.RateLimiting;

public sealed class RedisDistributedRateLimiterService(
    IRedisConnectionFactory connectionFactory,
    ILogger<RedisDistributedRateLimiterService> logger) : IDistributedRateLimiterService
{
    public async Task<bool> IsRateLimitedAsync(
        string policyName,
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = connectionFactory.GetDatabase();
            var cacheKey = CacheKeyBuilder.RateLimit(policyName, key);
            var windowSeconds = (long)window.TotalSeconds;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var windowStart = now - windowSeconds;

            var script = @"
                redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
                local count = redis.call('ZCARD', KEYS[1])
                if count < tonumber(ARGV[2]) then
                    redis.call('ZADD', KEYS[1], ARGV[3], ARGV[4])
                    redis.call('EXPIRE', KEYS[1], ARGV[5])
                    return 0
                end
                return 1";

            var member = $"{now}:{Guid.NewGuid():N}";
            var isLimited = (int)await db.ScriptEvaluateAsync(
                script,
                new RedisKey[] { cacheKey },
                new RedisValue[] { windowStart.ToString(), permitLimit.ToString(), now.ToString(), member, windowSeconds.ToString() });

            if (isLimited == 1)
            {
                logger.LogInformation(
                    "Rate limited policy={PolicyName} key={Key} limit={Limit} window={Window}s",
                    policyName, key, permitLimit, windowSeconds);
            }

            return isLimited == 1;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // Fail open: a rate-limiter backing-store outage (connection failure OR command
            // timeout) must not fail the request. Note RedisTimeoutException derives from
            // System.TimeoutException — NOT RedisException — so both must be caught here,
            // otherwise a Redis stall surfaces as an unhandled HTTP 500 on the auth path.
            logger.LogWarning(exception,
                "Redis rate limit check failed for policy={PolicyName} key={Key} — allowing request",
                policyName, key);
            return false;
        }
    }

    public async Task<int> GetCurrentCountAsync(
        string policyName,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = connectionFactory.GetDatabase();
            var cacheKey = CacheKeyBuilder.RateLimit(policyName, key);

            return (int)await db.SortedSetLengthAsync(cacheKey);
        }
        catch (RedisException)
        {
            return 0;
        }
    }

    public async Task ResetAsync(
        string policyName,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = connectionFactory.GetDatabase();
            var cacheKey = CacheKeyBuilder.RateLimit(policyName, key);
            await db.KeyDeleteAsync(cacheKey);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis rate limit reset failed for policy={PolicyName} key={Key}", policyName, key);
        }
    }
}
