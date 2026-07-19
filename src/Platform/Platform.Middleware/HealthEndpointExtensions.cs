using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Responses;

namespace Platform.Middleware;

public static class HealthEndpointExtensions
{
    public static IEndpointRouteBuilder MapPlatformHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", static () =>
        {
            var response = ApiResponse<HealthResponse>.Ok(new HealthResponse("Alive"), Guid.Empty);
            return TypedResults.Ok(response);
        })
        .AllowAnonymous()
        .WithName("Liveness")
        .WithTags("Health");

        endpoints.MapGet("/health/ready", async (
            IReadinessService readiness,
            IStartupCoordinator coordinator,
            CancellationToken ct) =>
        {
            var result = await readiness.CheckReadinessAsync(ct);
            var status = result.IsReady ? "Ready" : "NotReady";

            var componentDetails = result.Components.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    status = kv.Value.IsHealthy ? "Healthy" : "Unhealthy",
                    kv.Value.Message,
                    kv.Value.Duration
                });

            var startupInfo = new
            {
                coordinator.StartupStartedAt,
                coordinator.StartupCompletedAt,
                coordinator.StartupDuration,
                coordinator.IsReady,
                tasks = coordinator.TaskStatuses.Values.Select(t => new
                {
                    t.DisplayName,
                    status = t.Status.ToString(),
                    t.StartedAt,
                    t.FinishedAt,
                    t.Duration,
                    t.Exception
                }),
                timeline = coordinator.Timeline.Select(e => new
                {
                    e.Timestamp,
                    e.Message,
                    e.TaskName,
                    e.ElapsedSinceStartup
                })
            };

            var response = ApiResponse<HealthResponse>.Ok(
                new HealthResponse(status, componentDetails, startupInfo),
                Guid.Empty);

            return result.IsReady
                ? TypedResults.Ok(response)
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        })
        .AllowAnonymous()
        .WithName("Readiness")
        .WithTags("Health");

        return endpoints;
    }
}
