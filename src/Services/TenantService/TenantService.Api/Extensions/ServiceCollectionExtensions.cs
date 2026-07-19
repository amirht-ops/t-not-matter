using FluentValidation;
using MediatR;
using Platform.Behaviors;
using Platform.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TenantService.Application.Features.CreateTenant;
using TenantService.Application.Features.ChangeTenantStatus;
using TenantService.Application.Features.UpdateTenantPlan;
using TenantService.Application.Features.CreateDepartment;
using TenantService.Application.Features.GetDepartmentById;
using TenantService.Application.Features.UpdateDepartment;
using TenantService.Application.Features.DeleteDepartment;
using TenantService.Infrastructure;
namespace TenantService.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTenantService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantServiceInfrastructure(configuration);
        services.AddPlatformMiddleware(configuration);
        services.AddAuthorization();
        services.AddPlatformAuthorizationPolicies();

        services.AddOptions<JwtOptions>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

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
        services.AddHttpContextAccessor();

        services.AddValidatorsFromAssemblyContaining<CreateTenantCommandValidator>();
        services.AddValidatorsFromAssemblyContaining<ChangeTenantStatusCommandValidator>();
        services.AddValidatorsFromAssemblyContaining<UpdateTenantPlanCommandValidator>();

        services.AddPlatformBehaviors(configuration);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateTenantCommand>();
        });

        return services;
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
}

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
            failures.Add("Jwt:SigningKey is required and must be configured in the secret store.");

        if (options.SigningKey is { Length: < 32 })
            failures.Add("Jwt:SigningKey must be at least 32 characters for HMAC-SHA256 security.");

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add("Jwt:Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add("Jwt:Audience is required.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
