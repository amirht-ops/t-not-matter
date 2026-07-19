using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Infrastructure;
using Platform.Caching;
using Platform.Infrastructure.Outbox;
using PolicyService.Application.Abstractions;
using PolicyService.Infrastructure.Messaging.RabbitMq;
using PolicyService.Infrastructure.OpaClient;
using PolicyService.Infrastructure.Outbox;
using SharedKernel.Infrastructure.Messaging;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Migrations;
using PolicyService.Domain.Repositories;
using PolicyService.Infrastructure.Persistence;
using PolicyService.Infrastructure.Repositories;

namespace PolicyService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformInfrastructure();
        services.AddPlatformCaching(configuration);

        services.AddDbContextPool<PolicyDbContext>((sp, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("Policy"));
            options.AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
            options.ReplaceService<IMigrationsSqlGenerator, RlsMigrationsSqlGenerator>();
        });

        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IQuotaPolicyRepository, QuotaPolicyRepository>();
        services.AddScoped<IUsageLedgerRepository, UsageLedgerRepository>();
        services.AddScoped<IDebtLedgerRepository, DebtLedgerRepository>();
        services.AddScoped<IPrincipalHierarchyRepository, PrincipalHierarchyRepository>();

        services.AddOptions<OpaOptions>().BindConfiguration("Opa").ValidateOnStart();
        services.AddHttpClient<IOpaDataUpdater, OpaDataUpdater>((provider, client) =>
            client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<OpaOptions>>().Value.BaseUrl));

        services.AddScoped<IPolicyUnitOfWork, PolicyUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IPolicyUnitOfWork>());
        services.AddScoped<IPlatformOutboxRepository<PolicyDbContext>, OutboxRepository<PolicyDbContext>>();

        services.Configure<OutboxPublishPolicy>(configuration.GetSection("Outbox"));
        services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMq").ValidateOnStart();
        services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
