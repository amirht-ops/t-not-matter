using Microsoft.OpenApi;
using PolicyService.Api.Endpoints;
using PolicyService.Api.Extensions;
using PolicyService.Application;
using PolicyService.Infrastructure;
using PolicyService.Infrastructure.Persistence;
using Platform.Middleware;
using Serilog;
using Scalar.AspNetCore;
using SharedKernel.Infrastructure.Extensions;

namespace PolicyService.Api;

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
            Log.Information("Starting PolicyService API");

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
            builder.Services.AddPolicyApi(builder.Configuration);

            builder.Services.AddStartupCoordinator();
            builder.Services.AddDatabaseWarmupTask<PolicyDbContext>();
            builder.Services.AddRedisWarmupTask();

            var opaUrl = builder.Configuration.GetValue<string>("Opa:BaseUrl") ?? "http://localhost:8181";
            builder.Services.AddHttpClientWarmupTask(opaUrl);

            var jwtKey = builder.Configuration.GetValue<string>("Jwt:SigningKey") ?? string.Empty;
            var jwtIssuer = builder.Configuration.GetValue<string>("Jwt:Issuer") ?? "enterprise-auth-platform";
            var jwtAudience = builder.Configuration.GetValue<string>("Jwt:Audience") ?? "enterprise-services";
            builder.Services.AddJwtWarmupTask(jwtKey, jwtIssuer, jwtAudience);

            var app = builder.Build();

            await app.ApplyMigrationsAsync<PolicyDbContext>();

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

            app.MapPolicyEndpoints();
            app.MapQuotaPolicyEndpoints();
            app.MapSubscriptionEndpoints();
            app.MapConsumptionEndpoints();
            app.MapLedgerEndpoints();
            app.MapAdministrationEndpoints();
            app.MapPrincipalHierarchyEndpoints();

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "PolicyService API terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
