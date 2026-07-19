using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Behaviors;

public static class BehaviorServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformBehaviors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection("Behaviors").Get<BehaviorOptions>()
            ?? new BehaviorOptions();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TenantBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        if (options.EnableAuthorization)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));

        if (options.EnableRetry)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

        if (options.EnableUnitOfWork)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));

        if (options.EnableAudit)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        if (options.EnablePerformanceWarnings)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

        if (options.EnableIdempotency)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        if (options.EnableCaching)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        if (options.EnableMetrics)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));

        if (options.EnableFailClosed)
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FailClosedBehavior<,>));

        return services;
    }
}
