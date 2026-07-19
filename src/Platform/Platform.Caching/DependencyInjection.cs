using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Abstractions.Caching;
using Platform.Abstractions.Infrastructure;
using Platform.Abstractions.Locking;
using Platform.Caching.Configuration;
using Platform.Caching.Idempotency;
using Platform.Caching.Locking;
using Platform.Caching.RateLimiting;
using SharedKernel.Caching;

namespace Platform.Caching;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RedisOptions>()
            .BindConfiguration(RedisOptions.SectionName);

        services.AddOptions<CachingOptions>()
            .BindConfiguration(CachingOptions.SectionName);

        services.TryAddSingleton<IRedisConnectionFactory, RedisConnectionFactory>();
        services.TryAddSingleton<IDistributedCacheService, RedisCacheService>();
        services.TryAddSingleton<IDistributedLockService, RedisDistributedLockService>();
        services.TryAddSingleton<IDistributedRateLimiterService, RedisDistributedRateLimiterService>();
        services.TryAddSingleton<IIdempotencyStore, DistributedIdempotencyStore>();
        services.TryAddSingleton<IEventConsumerDeduplicationGuard, RedisEventConsumerDeduplicationGuard>();

        return services;
    }
}
