using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Caching;
using Platform.Caching.Configuration;
using SharedKernel.Caching;
using StackExchange.Redis;

namespace Platform.Caching;

public sealed class RedisCacheService : IDistributedCacheService
{
    private readonly IRedisConnectionFactory _connectionFactory;
    private readonly CachingOptions _options;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(
        IRedisConnectionFactory connectionFactory,
        IOptions<CachingOptions> options,
        ILogger<RedisCacheService> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _connectionFactory.GetDatabase();
            var value = await db.StringGetAsync(Key(key));
            if (!value.HasValue) return default;

            return JsonSerializer.Deserialize<T>((ReadOnlySpan<byte>)value!, JsonOptions);
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis GET failed for key {Key}", key);
            return default;
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis GET timed out for key {Key}", key);
            return default;
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;

        try
        {
            var db = _connectionFactory.GetDatabase();
            var data = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

            var expiry = options.AbsoluteExpirationRelativeToNow
                         ?? TimeSpan.FromSeconds(_options.DefaultTtlSeconds);

            await db.StringSetAsync(Key(key), data, expiry);
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis SET failed for key {Key}", key);
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis SET timed out for key {Key}", key);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _connectionFactory.GetDatabase();
            await db.KeyDeleteAsync(Key(key));
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis DEL failed for key {Key}", key);
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis DEL timed out for key {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _connectionFactory.GetDatabase();
            return await db.KeyExistsAsync(Key(key));
        }
        catch (RedisException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;

        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        bool acquired = false;

        try
        {
            await semaphore.WaitAsync(cancellationToken);
            acquired = true;

            cached = await GetAsync<T>(key, cancellationToken);
            if (cached is not null) return cached;

            var value = await factory(cancellationToken);
            if (value is not null)
            {
                await SetAsync(key, value, options, cancellationToken);
            }

            return value;
        }
        finally
        {
            if (acquired)
            {
                semaphore.Release();
                if (semaphore.CurrentCount == 1)
                    _locks.TryRemove(KeyValuePair.Create(key, semaphore));
            }
        }
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var server = GetServer();
            if (server is null) return;

            var db = _connectionFactory.GetDatabase();
            var fullPattern = Key(pattern);

            await foreach (var key in server.KeysAsync(pattern: fullPattern))
            {
                if (cancellationToken.IsCancellationRequested) break;
                await db.KeyDeleteAsync(key);
            }

            _logger.LogInformation("Cache invalidated by pattern {Pattern}", pattern);
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis pattern invalidation failed for {Pattern}", pattern);
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis pattern invalidation timed out for {Pattern}", pattern);
        }
    }

    private string Key(string key) => string.IsNullOrEmpty(_options.KeyPrefix) ? key : $"{_options.KeyPrefix}:{key}";

    private IServer? GetServer()
    {
        try
        {
            var connection = _connectionFactory.GetConnection();
            var endpoints = connection.GetEndPoints();
            return endpoints.Length > 0
                ? connection.GetServer(endpoints[0])
                : null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to get Redis server endpoint");
            return null;
        }
    }
}
