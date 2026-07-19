using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Abstractions.Middleware;

namespace Platform.Middleware;

public static class MiddlewareServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformMiddleware(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MiddlewareOptions>(
            configuration.GetSection(MiddlewareOptions.SectionName));

        services.Configure<RateLimitingOptions>(
            configuration.GetSection(RateLimitingOptions.SectionName));

        return services;
    }

    public static IApplicationBuilder UsePlatformMiddleware(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(MiddlewareOptions.SectionName)
            .Get<MiddlewareOptions>() ?? new MiddlewareOptions();

        if (options.EnableExceptionHandling)
            app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (options.EnableCorrelation)
            app.UseMiddleware<CorrelationMiddleware>();

        app.UseMiddleware<PrincipalResolutionMiddleware>();

        if (options.EnableTenantResolution)
            app.UseMiddleware<TenantMiddleware>();

        app.UseMiddleware<SerilogEnrichmentMiddleware>();

        if (options.EnableRequestLogging)
            app.UseMiddleware<RequestLoggingMiddleware>();

        if (options.EnableRateLimiting)
            app.UseMiddleware<RateLimitingMiddleware>();

        return app;
    }
}