using Microsoft.Extensions.Logging;
using Platform.Abstractions.Caching;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class RedisStartupTask : IStartupTask
{
    private readonly IRedisConnectionFactory _factory;
    private readonly ILogger<RedisStartupTask> _logger;

    public RedisStartupTask(
        IRedisConnectionFactory factory,
        ILogger<RedisStartupTask> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public string DisplayName => "Redis";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Warming up Redis connection");

        try
        {
            await _factory.InitializeAsync(cancellationToken);

            var db = _factory.GetDatabase();
            await db.PingAsync(StackExchange.Redis.CommandFlags.None);

            _logger.LogInformation("Redis connection verified");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis warmup failed; will retry on first use");
        }
    }
}
