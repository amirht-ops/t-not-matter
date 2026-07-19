using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Infrastructure;
using Platform.Abstractions.Principal;
using Platform.Caching;
using Platform.Infrastructure;
using Platform.Infrastructure.Outbox;
using Platform.Infrastructure.Persistence.Migrations;
using SharedKernel.Authorization;
using SharedKernel.Caching;
using SharedKernel.Infrastructure.Messaging;
using TenantService.Application.Common.Abstractions;
using TenantService.Domain.Repositories;
using TenantService.Infrastructure.Audit;
using Platform.Authorization;
using TenantService.Infrastructure.Caching;
using TenantService.Infrastructure.Messaging.Consumers;
using TenantService.Infrastructure.Messaging.RabbitMq;
using TenantService.Infrastructure.Outbox;
using TenantService.Infrastructure.Persistence;
using TenantService.Infrastructure.Persistence.Repositories;
using Polly;
using Polly.Extensions.Http;

namespace TenantService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTenantServiceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        AddOptions(services, configuration);
        AddPersistence(services, configuration);
        AddRepositories(services);
        AddOutbox(services);
        AddMessaging(services, configuration);
        AddCaching(services, configuration);
        AddAudit(services);
        AddAuthorizationClient(services, configuration);

        services.AddPlatformInfrastructure();

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Platform.Authorization.JwtOptions>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RabbitMqOptions>()
            .BindConfiguration("RabbitMq")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<OutboxPublishPolicy>(
            configuration.GetSection("Outbox"));

        services.Configure<AuthorizationServiceClientOptions>(
            configuration.GetSection(AuthorizationServiceClientOptions.SectionName));
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextPool<TenantDbContext>((sp, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("Tenant"));
            options.AddInterceptors(sp.GetRequiredService<Platform.Infrastructure.Persistence.TenantRlsInterceptor>());
            options.ReplaceService<IMigrationsSqlGenerator, RlsMigrationsSqlGenerator>();
        });
    }

    private static void AddRepositories(IServiceCollection services)
    {
        services.AddScoped<TenantRepository>();
        services.AddScoped<ITenantRepository>(provider =>
            new CachedTenantRepository(
                provider.GetRequiredService<TenantRepository>(),
                provider.GetRequiredService<IDistributedCacheService>()));

        services.AddScoped<DepartmentRepository>();
        services.AddScoped<IDepartmentRepository>(provider =>
            provider.GetRequiredService<DepartmentRepository>());
    }

    private static void AddOutbox(IServiceCollection services)
    {
        services.AddScoped<IPlatformOutboxRepository<TenantDbContext>, OutboxRepository<TenantDbContext>>();
        services.AddScoped<ITenantUnitOfWork, TenantUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ITenantUnitOfWork>());
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(configurator =>
        {
            configurator.AddConsumer<TenantCacheInvalidationConsumer>();

            configurator.UsingRabbitMq((context, cfg) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                var virtualHost = options.VirtualHost == "/" ? string.Empty : options.VirtualHost.TrimStart('/');
                cfg.Host(new Uri($"rabbitmq://{options.Host}:{options.Port}/{virtualHost}"), host =>
                {
                    host.Username(options.Username);
                    host.Password(options.Password);
                    host.PublisherConfirmation = true;
                });

                cfg.Message<SharedKernel.Contract.Events.EventEnvelope>(message =>
                    message.SetEntityName(options.ExchangeName));
                cfg.Publish<SharedKernel.Contract.Events.EventEnvelope>(publish =>
                {
                    publish.Durable = true;
                    publish.ExchangeType = "topic";
                });

                cfg.UseMessageRetry(r => r.Exponential(10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

                cfg.ReceiveEndpoint("tenant.cache", e =>
                {
                    e.ConfigureConsumer<TenantCacheInvalidationConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue("tenant.cache.dlx", "tenant.cache.dlq");
                });
            });
        });

        services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
        services.AddHostedService<TenantOutboxProcessor>();
    }

    private static void AddCaching(IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformCaching(configuration);
    }

    private static void AddAudit(IServiceCollection services)
    {
        services.AddScoped<TenantService.Application.Common.Abstractions.IAuditService, LoggingAuditService>();
    }

    private static void AddAuthorizationClient(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IServiceTokenGenerator>(sp =>
            new Platform.Authorization.PlatformServiceTokenGenerator(
                PlatformServiceName.Tenant.Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Platform.Authorization.JwtOptions>>(),
                sp.GetRequiredService<IPlatformServiceRegistry>()));
        services.AddTransient<Platform.Authorization.ServiceTokenDelegatingHandler>();

        services.AddHttpClient<IAuthorizationDecisionService, AuthorizationServiceClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<AuthorizationServiceClientOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            })
            .AddHttpMessageHandler<Platform.Authorization.ServiceTokenDelegatingHandler>()
            .AddPolicyHandler((sp, _) =>
            {
                var opts = sp.GetRequiredService<IOptions<AuthorizationServiceClientOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(opts.RetryCount, retryAttempt =>
                        TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100));
            })
            .AddPolicyHandler((sp, _) =>
            {
                var opts = sp.GetRequiredService<IOptions<AuthorizationServiceClientOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .CircuitBreakerAsync(
                        opts.CircuitBreakerThreshold,
                        TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds));
            });
    }
}
