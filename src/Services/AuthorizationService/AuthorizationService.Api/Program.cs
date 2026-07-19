using AuthorizationService.Api.Endpoints;
using AuthorizationService.Api.Extensions;
using AuthorizationService.Application;
using AuthorizationService.Infrastructure;
using AuthorizationService.Infrastructure.Persistence;
using Microsoft.OpenApi;
using Platform.Middleware;
using Serilog;
using Scalar.AspNetCore;
using SharedKernel.Infrastructure.Extensions;

namespace AuthorizationService.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
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
            Log.Information("Starting AuthorizationService API");

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
            builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
            builder.Services.AddAuthorizationApi(builder.Configuration);

            builder.Services.AddStartupCoordinator();
            builder.Services.AddDatabaseWarmupTask<AuthorizationDbContext>();
            builder.Services.AddRedisWarmupTask();

            var opaUrl = builder.Configuration.GetValue<string>("Opa:BaseUrl") ?? "http://localhost:8181";
            builder.Services.AddHttpClientWarmupTask(opaUrl);

            // Provision the base authorization Rego policy into OPA at startup.
            // OPA runs in-memory in the dev/test topology, so the decision rule
            // (data.authorization.allow) must be (re)loaded on every boot or all
            // OPA-backed decisions fail closed. RBAC/permission *data* is synced
            // separately by OpaSyncConsumer.
            builder.Services.AddStartupTask<AuthorizationService.Infrastructure.OpaClient.OpaPolicyProvisioningTask>();


            var jwtKey = builder.Configuration.GetValue<string>("Jwt:SigningKey") ?? string.Empty;
            var jwtIssuer = builder.Configuration.GetValue<string>("Jwt:Issuer") ?? "enterprise-auth-platform";
            var jwtAudience = builder.Configuration.GetValue<string>("Jwt:Audience") ?? "enterprise-services";
            builder.Services.AddJwtWarmupTask(jwtKey, jwtIssuer, jwtAudience);

            var app = builder.Build();

            await app.ApplyMigrationsAsync<AuthorizationDbContext>();
            await app.SeedAuthorizationDataAsync();

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

            app.MapAuthorizationEndpoints();
            app.MapRoleEndpoints();
            app.MapPermissionEndpoints();
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "AuthorizationService API terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
