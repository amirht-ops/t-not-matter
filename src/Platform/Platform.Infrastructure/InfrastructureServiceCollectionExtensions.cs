using Microsoft.Extensions.DependencyInjection;
using Platform.Abstractions.Infrastructure;
using Platform.Abstractions.Principal;
using Platform.Abstractions.Tenant;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Principal;
using Platform.Infrastructure.Tenant;

namespace Platform.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IRequestContextAccessor, RequestContextAccessor>();
        services.AddScoped<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        services.AddScoped<ICurrentPrincipalFactory, CurrentPrincipalFactory>();
        services.AddSingleton<IPlatformServiceRegistry, PlatformServiceRegistry>();
        services.AddSingleton<TenantRlsInterceptor>();

        return services;
    }
}
