using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using SharedKernel.Authorization;

namespace Platform.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AuthorizationServiceClientOptions>(
            configuration.GetSection(AuthorizationServiceClientOptions.SectionName));

        services.AddTransient<ServiceTokenDelegatingHandler>();

        services.AddHttpClient<IAuthorizationDecisionService, AuthorizationServiceClient>(
                (sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptions<AuthorizationServiceClientOptions>>().Value;
                    client.BaseAddress = new Uri(options.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                })
            .AddHttpMessageHandler<ServiceTokenDelegatingHandler>()
            .AddPolicyHandler((sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<AuthorizationServiceClientOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .Or<OperationCanceledException>()
                    .WaitAndRetryAsync(options.RetryCount, retryAttempt =>
                        TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100));
            })
            .AddPolicyHandler((sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<AuthorizationServiceClientOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .Or<OperationCanceledException>()
                    .CircuitBreakerAsync(
                        options.CircuitBreakerThreshold,
                        TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds));
            });

        return services;
    }
}
