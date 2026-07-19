using IdentityService.Api.Endpoints;
using IdentityService.Api.Extensions;
using IdentityService.Infrastructure.Persistence;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Platform.Middleware;
using Serilog;
using SharedKernel.Infrastructure.Extensions;

namespace IdentityService.Api;

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
            Log.Information("Starting IdentityService API");

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
            builder.Services.AddIdentityService(builder.Configuration);

            builder.Services.AddStartupCoordinator();
            builder.Services.AddDatabaseWarmupTask<IdentityDbContext>();
            builder.Services.AddRedisWarmupTask();

            var authzUrl = builder.Configuration.GetValue<string>("Services:AuthorizationService:BaseUrl") ?? "http://localhost:5108";
            var tenantUrl = builder.Configuration.GetValue<string>("Services:TenantService:BaseUrl") ?? "http://localhost:5002";
            builder.Services.AddHttpClientWarmupTask(authzUrl, tenantUrl);

            var jwtKey = builder.Configuration.GetValue<string>("Jwt:SigningKey") ?? "";
            var jwtIssuer = builder.Configuration.GetValue<string>("Jwt:Issuer") ?? "enterprise-auth-platform";
            var jwtAudience = builder.Configuration.GetValue<string>("Jwt:Audience") ?? "enterprise-services";
            builder.Services.AddJwtWarmupTask(jwtKey, jwtIssuer, jwtAudience);

            var app = builder.Build();

            await app.ApplyMigrationsAsync<IdentityDbContext>();
            await app.SeedIdentityDataAsync();

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

            app.MapAuthEndpoints();
            app.MapSessionEndpoints();
            app.MapUserEndpoints();

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "IdentityService API terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
