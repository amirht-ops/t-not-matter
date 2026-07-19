using StackExchange.Redis;

namespace Platform.Abstractions.Caching;

public interface IRedisConnectionFactory
{
    IConnectionMultiplexer GetConnection();
    IDatabase GetDatabase();
    bool IsConnected { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
