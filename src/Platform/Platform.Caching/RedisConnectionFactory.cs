using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Caching;
using Platform.Caching.Configuration;
using StackExchange.Redis;

namespace Platform.Caching;

public sealed class RedisConnectionFactory : IRedisConnectionFactory, IDisposable
{
    private readonly RedisOptions _options;
    private readonly ILogger<RedisConnectionFactory> _logger;
    private readonly object _lock = new();
    private IConnectionMultiplexer? _connection;
    private bool _disposed;
    private bool _initialized;

    public RedisConnectionFactory(
        IOptions<RedisOptions> options,
        ILogger<RedisConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        var connection = await CreateConnectionAsync(cancellationToken);

        lock (_lock)
        {
            if (_initialized)
            {
                connection.Dispose();
                return;
            }

            _connection?.Dispose();
            _connection = connection;
            _initialized = true;
        }
    }

    public IConnectionMultiplexer GetConnection()
    {
        // A StackExchange.Redis multiplexer is designed to be created once and reused for the
        // lifetime of the application. With AbortOnConnectFail=false it stays usable during a
        // backing-store outage and reconnects itself in the background once Redis returns.
        //
        // We therefore return the existing multiplexer even when it is momentarily
        // disconnected, rather than disposing and rebuilding it. Rebuilding on every transient
        // IsConnected==false (under concurrent health-probe + request traffic during an outage)
        // races multiple half-connected multiplexers against each other and can leave the
        // service permanently wedged after Redis recovers — observed in Phase 10 chaos testing.
        var existing = _connection;
        if (existing is not null)
            return existing;

        lock (_lock)
        {
            if (_connection is not null)
                return _connection;

            _connection = CreateConnection();
            _initialized = true;
            return _connection;
        }
    }

    public IDatabase GetDatabase()
    {
        var connection = GetConnection();
        return connection.GetDatabase(_options.DatabaseId);
    }

    public bool IsConnected => _connection is { IsConnected: true };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection?.Dispose();
        _connection = null;
    }

    private static ConfigurationOptions BuildConfig(RedisOptions options)
    {
        var config = new ConfigurationOptions
        {
            ConnectTimeout = options.ConnectTimeoutMs,
            SyncTimeout = options.SyncTimeoutMs,
            AbortOnConnectFail = options.AbortOnConnectFail,
            Ssl = options.Ssl,
            Password = options.Password,
            ConnectRetry = options.RetryCount
        };

        config.EndPoints.Add(options.ConnectionString);
        return config;
    }

    private void AttachHandlers(ConnectionMultiplexer connection)
    {
        connection.ConnectionFailed += (_, args) =>
            _logger.LogError(args.Exception,
                "Redis connection failed: {EndPoint} ({FailureType})",
                args.EndPoint, args.FailureType);

        connection.ConnectionRestored += (_, args) =>
            _logger.LogInformation(
                "Redis connection restored: {EndPoint}", args.EndPoint);

        connection.ErrorMessage += (_, args) =>
            _logger.LogWarning(
                "Redis error: {EndPoint} - {Message}", args.EndPoint, args.Message);
    }

    private ConnectionMultiplexer CreateConnection()
    {
        _logger.LogInformation(
            "Creating Redis connection (sync) to {Endpoint} (Db={Database}, Timeout={Timeout}ms)",
            _options.ConnectionString, _options.DatabaseId, _options.ConnectTimeoutMs);

        var connection = ConnectionMultiplexer.Connect(BuildConfig(_options));
        AttachHandlers(connection);
        return connection;
    }

    private async Task<ConnectionMultiplexer> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating Redis connection (async) to {Endpoint} (Db={Database}, Timeout={Timeout}ms)",
            _options.ConnectionString, _options.DatabaseId, _options.ConnectTimeoutMs);

        var connection = await ConnectionMultiplexer.ConnectAsync(BuildConfig(_options));
        AttachHandlers(connection);
        return connection;
    }
}
