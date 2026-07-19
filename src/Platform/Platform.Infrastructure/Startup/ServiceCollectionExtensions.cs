using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Abstractions.Infrastructure;
using Platform.Infrastructure.Startup;

namespace Microsoft.Extensions.DependencyInjection;

public static class StartupServiceCollectionExtensions
{
    private static readonly DbContextTypesRegistry _dbContextRegistry = new();

    public static DbContextTypesRegistry DbContextRegistry => _dbContextRegistry;

    public static IServiceCollection AddStartupCoordinator(this IServiceCollection services)
    {
        services.TryAddSingleton<StartupCoordinator>();
        services.AddSingleton<IStartupCoordinator>(sp => sp.GetRequiredService<StartupCoordinator>());
        services.AddHostedService<StartupTaskRunner>();
        services.AddSingleton(_dbContextRegistry);
        services.AddSingleton<ReadinessState>();
        services.AddSingleton<IReadinessService, ReadinessService>();
        services.AddHostedService<ReadinessMonitor>();

        services.Configure<StartupTaskOptions>(opts =>
        {
            foreach (var kv in StartupTaskDefaults.DefaultPolicies)
            {
                if (!opts.Tasks.ContainsKey(kv.Key))
                    opts.Tasks[kv.Key] = kv.Value;
            }
        });

        return services;
    }

    public static IServiceCollection AddStartupTask<T>(this IServiceCollection services,
        Action<StartupTaskPolicy>? configurePolicy = null)
        where T : class, IStartupTask
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, T>());

        if (configurePolicy is not null)
        {
            services.Configure<StartupTaskOptions>(opts =>
            {
                var key = typeof(T).Name;
                if (!opts.Tasks.TryGetValue(key, out var policy))
                {
                    policy = new StartupTaskPolicy();
                    opts.Tasks[key] = policy;
                }
                configurePolicy(policy);
            });
        }

        return services;
    }

    public static IServiceCollection AddDatabaseWarmupTask<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddStartupTask<DatabaseStartupTask<TDbContext>>();
        _dbContextRegistry.Register(typeof(TDbContext));
        return services;
    }

    public static IServiceCollection AddRedisWarmupTask(this IServiceCollection services)
    {
        services.AddStartupTask<RedisStartupTask>();
        return services;
    }

    public static IServiceCollection AddHttpClientWarmupTask(this IServiceCollection services, params string[] baseUrls)
    {
        services.Configure<HttpClientWarmupOptions>(opts =>
        {
            foreach (var url in baseUrls)
            {
                opts.BaseUrls.Add(url);
            }
        });
        services.AddStartupTask<HttpClientWarmupTask>();
        return services;
    }

    public static IServiceCollection AddJwtWarmupTask(this IServiceCollection services,
        string signingKey,
        string issuer,
        string audience)
    {
        services.AddSingleton(new JwtWarmupOptions
        {
            SigningKey = signingKey,
            Issuer = issuer,
            Audience = audience
        });
        services.AddStartupTask<JwtWarmupTask>();
        return services;
    }
}
