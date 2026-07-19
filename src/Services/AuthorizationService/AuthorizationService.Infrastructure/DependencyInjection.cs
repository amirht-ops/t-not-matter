using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Repositories;
using Platform.Abstractions.Caching;
using Platform.Abstractions.Infrastructure;
using AuthorizationService.Domain.Services;
using AuthorizationService.Infrastructure.Analytics;
using AuthorizationService.Infrastructure.Audit;
using AuthorizationService.Infrastructure.Caching;
using AuthorizationService.Infrastructure.OpaClient;
using AuthorizationService.Infrastructure.Services;
using AuthorizationService.Infrastructure.Outbox;
using AuthorizationService.Infrastructure.Persistence;
using AuthorizationService.Infrastructure.Persistence.Repositories;
using AuthorizationService.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Principal;
using Platform.Authorization;
using Platform.Caching;
using Platform.Infrastructure;
using Platform.Infrastructure.Outbox;
using Platform.Infrastructure.Persistence.Migrations;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;

namespace AuthorizationService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Platform.Authorization.JwtOptions>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<OpaOptions>().BindConfiguration("Opa").ValidateOnStart();
        services.AddOptions<AuthorizationCacheOptions>().BindConfiguration("AuthorizationCache");
        services.Configure<OutboxPublishPolicy>(configuration.GetSection("Outbox"));

        var redisConnectionString = configuration.GetValue<string>("Redis:ConnectionString") ??
                                   configuration.GetConnectionString("Redis") ??
                                   "localhost:6379";
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "authz";
        });
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            sp.GetRequiredService<IRedisConnectionFactory>().GetConnection());
        services.AddPlatformInfrastructure();
        services.AddPlatformCaching(configuration);

        services.AddDbContextPool<AuthorizationDbContext>((sp, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("Authorization"));
            options.AddInterceptors(sp.GetRequiredService<Platform.Infrastructure.Persistence.TenantRlsInterceptor>());
            options.ReplaceService<IMigrationsSqlGenerator, RlsMigrationsSqlGenerator>();
        });

        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRoleAssignmentRepository, RoleAssignmentRepository>();
        services.AddScoped<IAuthorizationUnitOfWork, AuthorizationUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IAuthorizationUnitOfWork>());
        services.AddScoped<IPlatformOutboxRepository<AuthorizationDbContext>, OutboxRepository<AuthorizationDbContext>>();

        services.AddSingleton<IAuthorizationCache, RedisAuthorizationCache>();
        services.AddSingleton<IRealTimeUsageStore, RedisRealTimeUsageStore>();
        services.AddScoped<IEffectivePermissionResolver, EffectivePermissionResolver>();
        services.AddSingleton<IAuthorizationAuditSink, LoggingAuthorizationAuditSink>();

        services.AddHttpClient<OpaHttpClient>((provider, client) =>
            {
                var opaOptions = provider.GetRequiredService<IOptions<OpaOptions>>().Value;
                client.BaseAddress = new Uri(opaOptions.BaseUrl);
                client.Timeout = TimeSpan.FromMilliseconds(opaOptions.TimeoutMs * 2);
            })
            .AddPolicyHandler((provider, _) =>
            {
                var opaOptions = provider.GetRequiredService<IOptions<OpaOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .Or<OperationCanceledException>()
                    .WaitAndRetryAsync(opaOptions.RetryCount, retryAttempt =>
                        TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100));
            })
            .AddPolicyHandler((provider, _) =>
            {
                var opaOptions = provider.GetRequiredService<IOptions<OpaOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .Or<OperationCanceledException>()
                    .CircuitBreakerAsync(
                        opaOptions.CircuitBreakerThreshold,
                        TimeSpan.FromSeconds(opaOptions.CircuitBreakerDurationSeconds));
            });
        services.AddScoped<IPolicyEvaluationGateway, OpaPolicyEvaluationGateway>();
        services.AddScoped<IAuthorizationDecisionEngine, AuthorizationDecisionService>();

        services.AddScoped<IUsageTrackingRepository, UsageTrackingRepository>();
        services.AddScoped<CacheInvalidationConsumer>();
        services.AddHostedService<OutboxProcessor>();
        services.AddHostedService<MonthlyQuotaResetJob>();

        services.AddHttpClient<IOpaDataUpdater, OpaDataUpdater>((provider, client) =>
            client.BaseAddress = new Uri(
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpaOptions>>().Value.BaseUrl));

        services.AddSingleton<IServiceTokenGenerator>(sp =>
            new PlatformServiceTokenGenerator(
                PlatformServiceName.Authorization.Value,
                sp.GetRequiredService<IOptions<Platform.Authorization.JwtOptions>>(),
                sp.GetRequiredService<IPlatformServiceRegistry>()));

        services.AddTransient<ServiceTokenDelegatingHandler>();

        services.AddHttpClient<ITenantServiceClient, TenantServiceClient>((provider, client) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            client.BaseAddress = new Uri(configuration.GetValue<string>("Services:TenantService:BaseUrl") ?? "http://localhost:5068");
            client.Timeout = TimeSpan.FromMilliseconds(configuration.GetValue<int>("Services:TenantService:TimeoutMs", 5000));
        }).AddHttpMessageHandler<ServiceTokenDelegatingHandler>();

        services.AddSingleton<IAnalyticsEventSink, AnalyticsEventSink>();

        return services;
    }
}
