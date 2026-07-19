using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Platform.Behaviors;
using Platform.Middleware;
using PolicyService.Infrastructure.Messaging.Consumers;
using PolicyService.Infrastructure.Messaging.RabbitMq;
using SharedKernel.Contract.Events;

namespace PolicyService.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPolicyApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformMiddleware(configuration);
        services.AddPlatformBehaviors(configuration);

        services.AddOptions<JwtOptions>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, IHostEnvironment>((options, jwtOptionsAccessor, env) =>
            {
                var jwtOptions = jwtOptionsAccessor.Value;
                options.RequireHttpsMetadata = !env.IsDevelopment();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "sub"
                };
            });

        services.AddAuthorization();
        services.AddPlatformAuthorizationPolicies();

        services.AddHttpContextAccessor();
        services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMq").ValidateOnStart();

        services.AddMassTransit(configurator =>
        {
            configurator.AddConsumer<PrincipalHierarchyConsumer>();
            configurator.AddConsumer<OpaSyncConsumer>();
            configurator.AddConsumer<CacheInvalidationConsumer>();

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

                cfg.Message<EventEnvelope>(message => message.SetEntityName(options.ExchangeName));
                cfg.Publish<EventEnvelope>(publish =>
                {
                    publish.Durable = true;
                    publish.ExchangeType = "topic";
                });

                cfg.UseMessageRetry(r => r.Exponential(10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

                const string dlxSuffix = ".dlx";
                const string dlqSuffix = ".dlq";

                cfg.ReceiveEndpoint("policy.principal-hierarchy", e =>
                {
                    e.ConfigureConsumer<PrincipalHierarchyConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"policy.principal-hierarchy{dlxSuffix}", $"policy.principal-hierarchy{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("policy.opa-sync", e =>
                {
                    e.ConfigureConsumer<OpaSyncConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"policy.opa-sync{dlxSuffix}", $"policy.opa-sync{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("policy.cache-invalidation", e =>
                {
                    e.ConfigureConsumer<CacheInvalidationConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"policy.cache-invalidation{dlxSuffix}", $"policy.cache-invalidation{dlqSuffix}");
                });
            });
        });

        return services;
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "enterprise-auth-platform";
    public string Audience { get; init; } = "enterprise-services";

    [System.ComponentModel.DataAnnotations.Required]
    public string SigningKey { get; init; } = string.Empty;
}
