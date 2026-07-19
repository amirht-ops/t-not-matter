using System.Text.Json;
using Platform.Abstractions.Caching;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Caching;
using StackExchange.Redis;

namespace Platform.Caching.Idempotency;

public sealed class DistributedIdempotencyStore(
    IRedisConnectionFactory redisConnectionFactory) : IIdempotencyStore
{
    private const string InProgressState = "in-progress";
    private const string CompletedState = "completed";
    private const string LegacyInProgressMarker = "1";

    private static readonly CacheEntryOptions EntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        FailSafe = true
    };
    private static readonly TimeSpan EntryTtl = EntryOptions.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<bool> TryRegisterAsync(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyBuilder.Idempotency(key);
        var envelope = JsonSerializer.SerializeToUtf8Bytes(
            new IdempotencyEnvelope<JsonElement?>(InProgressState, null),
            JsonOptions);

        var db = redisConnectionFactory.GetDatabase();
        var acquired = await db.StringSetAsync(
            cacheKey,
            value: envelope,
            expiry: EntryTtl,
            when: When.NotExists);

        return acquired;
    }

    public async Task<T?> GetResponseAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyBuilder.Idempotency(key);
        try
        {
            var db = redisConnectionFactory.GetDatabase();
            var value = await db.StringGetAsync(cacheKey);
            if (!value.HasValue)
                return default;

            if (value == LegacyInProgressMarker)
                return default;

            var envelope = JsonSerializer.Deserialize<IdempotencyEnvelope<JsonElement>>(
                (ReadOnlySpan<byte>)value!,
                JsonOptions);

            if (envelope?.State != CompletedState)
                return default;

            return envelope.Response.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (RedisException)
        {
            return default;
        }
        catch (TimeoutException)
        {
            return default;
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }

    public async Task StoreResponseAsync<T>(string key, T response, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyBuilder.Idempotency(key);
        var db = redisConnectionFactory.GetDatabase();
        var envelope = JsonSerializer.SerializeToUtf8Bytes(
            new IdempotencyEnvelope<T>(CompletedState, response),
            JsonOptions);

        await db.StringSetAsync(
            cacheKey,
            value: envelope,
            expiry: EntryTtl);
    }

    private sealed record IdempotencyEnvelope<T>(string State, T Response);
}
