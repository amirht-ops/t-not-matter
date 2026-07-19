using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Platform.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int ThresholdMs = 20000;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > ThresholdMs)
        {
            logger.LogWarning(
                "Long running request: {RequestName} ({ElapsedMs}ms) exceeded threshold {ThresholdMs}ms",
                typeof(TRequest).Name,
                stopwatch.ElapsedMilliseconds,
                ThresholdMs);
        }

        return response;
    }
}
