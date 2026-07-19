using System.Text;
using FluentValidation;
using IdentityService.Api.Serialization;
using IdentityService.Api.Validation;
using IdentityService.Application.Common.Abstractions;
using IdentityService.Infrastructure.Messaging.Consumers;
using Platform.Abstractions.Infrastructure;
using Platform.Behaviors;
using Platform.Caching;
using Platform.Infrastructure;
using Platform.Infrastructure.Outbox;
using Platform.Middleware;
using Microsoft.Extensions.Hosting;
using SharedKernel.Authorization;
using SharedKernel.Caching;
using IdentityService.Application.Features.Login;
using IdentityService.Application.Features.Register;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.Services;
using IdentityService.Infrastructure.Audit;
using IdentityService.Infrastructure.Crypto;
using Platform.Abstractions.Authorization;
using IdentityService.Infrastructure.Messaging.RabbitMq;
using IdentityService.Infrastructure.Mfa;
using IdentityService.Infrastructure.Outbox;
using IdentityService.Infrastructure.Persistence;
using IdentityService.Infrastructure.Persistence.Repositories;
using IdentityService.Infrastructure.Services;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using SharedKernel.Infrastructure.Messaging;
using SharedKernel.Infrastructure.Outbox;
using IdentityService.Domain.Aggregates.User;
using IdentityService.Infrastructure.Caching;
using IdentityService.Infrastructure.Options;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Migrations;
using Platform.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IdentityService.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Platform.Authorization.JwtOptions>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<JwtOptions>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<RabbitMqOptions>()
            .BindConfiguration("RabbitMq")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<OutboxPublishPolicy>()
            .BindConfiguration("Outbox")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddDataProtection();
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
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSessionAsync
                };
            });
        services.AddAuthorization();
        services.AddPlatformAuthorizationPolicies();
        services.AddPlatformMiddleware(configuration);
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("AuthenticationLimiter", config =>
            {
                config.PermitLimit = 10;
                config.Window = TimeSpan.FromMinutes(1);
                config.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                config.QueueLimit = 2;
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, IdentityApiJsonSerializerContext.Default));
        services.AddPlatformCaching(configuration);
        services.AddHttpContextAccessor();
        services.AddPlatformInfrastructure();
        services.AddDbContextPool<IdentityDbContext>((sp, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("Identity"));
            options.AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
            options.ReplaceService<IMigrationsSqlGenerator, RlsMigrationsSqlGenerator>();
        });
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
        services.AddValidatorsFromAssemblyContaining<RegisterCommandValidator>();
        services.AddScoped<UserRepository>();
        services.AddScoped<IUserRepository>(provider =>
            new CachedUserRepository(provider.GetRequiredService<UserRepository>(),
                provider.GetRequiredService<IDistributedCacheService>()));
        services.AddScoped<SessionRepository>();
        services.AddScoped<ISessionRepository>(provider =>
            new CachedSessionRepository(provider.GetRequiredService<SessionRepository>(),
                provider.GetRequiredService<IDistributedCacheService>()));
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IPlatformOutboxRepository<IdentityDbContext>, OutboxRepository<IdentityDbContext>>();
        services.AddScoped<IAccessRiskRepository, AccessRiskRepository>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IIdentityUnitOfWork>());
        services.AddScoped<AuthenticationPolicy>();
        services.AddMassTransit(configuration =>
        {
            configuration.AddConsumer<TenantStatusChangedConsumer>();
            configuration.AddConsumer<TenantCreatedCacheInvalidationConsumer>();
            configuration.AddConsumer<AuthorizationRoleAssignedConsumer>();
            configuration.UsingRabbitMq((context, cfg) =>
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

                const string dlxSuffix = ".dlx";
                const string dlqSuffix = ".dlq";

                cfg.ReceiveEndpoint("identity.tenant-status-changed", e =>
                {
                    e.ConfigureConsumer<TenantStatusChangedConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"identity.tenant-status-changed{dlxSuffix}", $"identity.tenant-status-changed{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("identity.tenant-created-cache-invalidation", e =>
                {
                    e.ConfigureConsumer<TenantCreatedCacheInvalidationConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"identity.tenant-created-cache-invalidation{dlxSuffix}", $"identity.tenant-created-cache-invalidation{dlqSuffix}");
                });
                cfg.ReceiveEndpoint("identity.authorization-role-assigned", e =>
                {
                    e.ConfigureConsumer<AuthorizationRoleAssignedConsumer>(context);
                    e.SetQuorumQueue();
                    e.BindDeadLetterQueue($"identity.authorization-role-assigned{dlxSuffix}", $"identity.authorization-role-assigned{dlqSuffix}");
                });
            });
        });
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenGenerator, HmacJwtTokenGenerator>();
        services.AddSingleton<IMfaProvider, TotpMfaProvider>();
        services.AddSingleton<IMfaSecretProtector, DataProtectionMfaSecretProtector>();
        services.AddOptions<Platform.Authorization.AuthorizationServiceClientOptions>()
            .BindConfiguration(Platform.Authorization.AuthorizationServiceClientOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<Platform.Abstractions.Authorization.IServiceTokenGenerator>(sp =>
            new Platform.Authorization.PlatformServiceTokenGenerator(
                Platform.Abstractions.Principal.PlatformServiceName.Identity.Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Platform.Authorization.JwtOptions>>(),
                sp.GetRequiredService<Platform.Abstractions.Principal.IPlatformServiceRegistry>()));
        services.AddTransient<Platform.Authorization.ServiceTokenDelegatingHandler>();
        services.AddHttpClient<IAuthorizationDecisionService, Platform.Authorization.AuthorizationServiceClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<Platform.Authorization.AuthorizationServiceClientOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            })
            .AddHttpMessageHandler<Platform.Authorization.ServiceTokenDelegatingHandler>()
            .AddPolicyHandler((sp, _) =>
            {
                var opts = sp.GetRequiredService<IOptions<Platform.Authorization.AuthorizationServiceClientOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(opts.RetryCount, retryAttempt =>
                        TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100));
            })
            .AddPolicyHandler((sp, _) =>
            {
                var opts = sp.GetRequiredService<IOptions<Platform.Authorization.AuthorizationServiceClientOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .CircuitBreakerAsync(
                        opts.CircuitBreakerThreshold,
                        TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds));
            });
        services.AddScoped<IMessagePublisher, RabbitMqMessagePublisher>();
        services.AddHostedService<IdentityOutboxDispatcher>();
        services.AddSingleton<IAuditSink, LoggingAuditSink>();
        services.AddHttpClient<ITenantServiceClient, TenantServiceClient>(client =>
            {
                client.BaseAddress = new Uri(configuration.GetValue<string>("Services:TenantService:BaseUrl") ??
                                             "http://localhost:5002");
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddHttpMessageHandler<Platform.Authorization.ServiceTokenDelegatingHandler>()
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100)))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)));
        services.AddOptions<AuthorizationRoleResolverOptions>()
            .BindConfiguration(AuthorizationRoleResolverOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<AuthorizationRoleResolver>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<AuthorizationRoleResolverOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            })
            .AddHttpMessageHandler<Platform.Authorization.ServiceTokenDelegatingHandler>()
            .AddPolicyHandler((sp, _) =>
            {
                var opts = sp.GetRequiredService<IOptions<AuthorizationRoleResolverOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(opts.RetryCount, retryAttempt =>
                        TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100));
            })
            .AddPolicyHandler((sp, _) =>
            {
                var opts = sp.GetRequiredService<IOptions<AuthorizationRoleResolverOptions>>().Value;
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .CircuitBreakerAsync(
                        opts.CircuitBreakerThreshold,
                        TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds));
            });
        services.AddScoped<IAuthorizationRoleResolver>(sp =>
            sp.GetRequiredService<AuthorizationRoleResolver>());
        services.AddPlatformBehaviors(configuration);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<LoginCommand>();
        });
        return services;
    }

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            context.Fail("Principal is missing.");
            return;
        }

        var principalType = principal.FindFirst("principal_type")?.Value;
        if (string.Equals(principalType, "service", StringComparison.OrdinalIgnoreCase))
        {
            // Service-to-service tokens are not bound to a user session; accept without session validation.
            if (TryGetGuidClaim(principal, "tenant_id", out var serviceTenantId))
                context.HttpContext.Items["TenantId"] = serviceTenantId;
            return;
        }

        if (!TryGetGuidClaim(principal, "tenant_id", out var tenantId) ||
            !TryGetGuidClaim(principal, "sub", out var userId) ||
            !TryGetGuidClaim(principal, "session_id", out var sessionId))
        {
            context.Fail("Required identity claims are missing.");
            return;
        }

        context.HttpContext.Items["TenantId"] = tenantId;

        var dbContext = context.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
        var session = await dbContext.Sessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == sessionId,
                context.HttpContext.RequestAborted);

        if (session is null || session.UserId != userId || !session.IsActive)
        {
            context.Fail("Session is not active.");
            return;
        }

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == userId,
                context.HttpContext.RequestAborted);

        if (user is null || user.IsDeleted || user.Status != UserStatus.Active)
        {
            context.Fail("User is not active.");
        }
    }

    private static bool TryGetGuidClaim(System.Security.Claims.ClaimsPrincipal principal, string claimType,
        out Guid value)
    {
        var raw = principal.FindFirst(claimType)?.Value;
        if (raw is null && claimType == "sub")
            raw = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out value) && value != Guid.Empty;
    }
}
