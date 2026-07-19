using System.ComponentModel.DataAnnotations;
using System.Text;
using AuthorizationService.Api.Validation;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Infrastructure.Messaging.Consumers;
using AuthorizationService.Infrastructure.Messaging.RabbitMq;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Platform.Behaviors;
using Platform.Middleware;
using SharedKernel.Infrastructure.Messaging;

namespace AuthorizationService.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAuthorizationApi(this IServiceCollection services, IConfiguration configuration)
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
        services.AddSingleton<IClock, SystemClock>();
        services.AddValidatorsFromAssemblyContaining<EvaluateAuthorizationRequestValidator>();

        services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMq").ValidateOnStart();

        services.AddMassTransit(configurator =>
        {
            configurator.AddConsumer<AuthorizationCacheInvalidationConsumer>();
            configurator.AddConsumer<AuditPipelineConsumer>();
            configurator.AddConsumer<OpaSyncConsumer>();
            configurator.AddConsumer<AnalyticsPipelineConsumer>();
            configurator.AddConsumer<DepartmentSoftDeleteConsumer>();

            configurator.UsingRabbitMq((context, cfg) =>
            {
                var options = context.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
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

                const string dlxSuffix = ".dlx";
                const string dlqSuffix = ".dlq";

                cfg.ReceiveEndpoint("authorization.cache", e =>
                {
                    e.ConfigureConsumer<AuthorizationCacheInvalidationConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"authorization.cache{dlxSuffix}", $"authorization.cache{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("audit.pipeline", e =>
                {
                    e.ConfigureConsumer<AuditPipelineConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"audit.pipeline{dlxSuffix}", $"audit.pipeline{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("opa.sync", e =>
                {
                    e.ConfigureConsumer<OpaSyncConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"opa.sync{dlxSuffix}", $"opa.sync{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("analytics.pipeline", e =>
                {
                    e.ConfigureConsumer<AnalyticsPipelineConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"analytics.pipeline{dlxSuffix}", $"analytics.pipeline{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("authorization.department-soft-delete", e =>
                {
                    e.ConfigureConsumer<DepartmentSoftDeleteConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"authorization.department-soft-delete{dlxSuffix}", $"authorization.department-soft-delete{dlqSuffix}");
                });
            });
        });

        services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();

        return services;
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "enterprise-auth-platform";
    public string Audience { get; init; } = "enterprise-services";

    [Required]
    public string SigningKey { get; init; } = string.Empty;
}
