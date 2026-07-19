using Microsoft.OpenApi;
using SharedKernel.Infrastructure.Extensions;
using TenantService.Api.Endpoints;
using TenantService.Api.Extensions;
using TenantService.Infrastructure.Persistence;
using Platform.Middleware;
using Serilog;
using Scalar.AspNetCore;

namespace TenantService.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build())
            .Enrich.FromLogContext()
            .Enrich.WithCorrelationId()
            .Enrich.WithThreadName()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .CreateLogger();

        try
        {
            Log.Information("Starting TenantService API");

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddOpenApi(options =>
            {
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Components ??= new OpenApiComponents();
                    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

                    document.Components.SecuritySchemes["TenantHeader"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Name = "X-Tenant-Id",
                        Description = "Tenant identifier (GUID)"
                    };

                    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "Enter your JWT token here (the 'Bearer ' prefix is added automatically)"
                    };

                    if (document.Paths is not null)
                    {
                        foreach (var path in document.Paths.Values)
                        {
                            if (path.Operations is not null)
                            {
                                foreach (var operation in path.Operations.Values)
                                {
                                    operation.Security ??= new List<OpenApiSecurityRequirement>();

                                    operation.Security.Add(new OpenApiSecurityRequirement
                                    {
                                        [new OpenApiSecuritySchemeReference("TenantHeader", document)] = [],
                                        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                                    });
                                }
                            }
                        }
                    }

                    return Task.CompletedTask;
                });
            });

            builder.Services.AddProblemDetails();
            builder.Services.AddTenantService(builder.Configuration);

            builder.Services.AddStartupCoordinator();
            builder.Services.AddDatabaseWarmupTask<TenantDbContext>();
            builder.Services.AddRedisWarmupTask();

            var authzUrl = builder.Configuration["Services:AuthorizationService:BaseUrl"]
                ?? throw new InvalidOperationException(
                    "Configuration 'Services:AuthorizationService:BaseUrl' is required. " +
                    "Set it in appsettings, environment variables, or the secret store.");
            builder.Services.AddHttpClientWarmupTask(authzUrl);

            var jwtKey = builder.Configuration["Jwt:SigningKey"]
                ?? throw new InvalidOperationException(
                    "Configuration 'Jwt:SigningKey' is required and must be set in the secret store. " +
                    "Do not use default values in production.");
            var jwtIssuer = builder.Configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException(
                    "Configuration 'Jwt:Issuer' is required. Set it in appsettings or the secret store.");
            var jwtAudience = builder.Configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException(
                    "Configuration 'Jwt:Audience' is required. Set it in appsettings or the secret store.");
            builder.Services.AddJwtWarmupTask(jwtKey, jwtIssuer, jwtAudience);

            var app = builder.Build();

            await app.ApplyMigrationsAsync<TenantDbContext>();
            await app.SeedTenantDataAsync();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference();
            }

            app.UseSerilogRequestLogging();
            app.UseAuthentication();
            app.UsePlatformMiddleware(builder.Configuration);

            app.MapPlatformHealthEndpoints();
            app.UseAuthorization();

            app.MapTenantEndpoints();
            app.MapDepartmentEndpoints();
            app.MapUserTenantEndpoints();

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "TenantService API terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
