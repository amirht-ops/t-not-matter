using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Platform.Behaviors;

public sealed class MetricsBehavior<TRequest, TResponse>(
    ILogger<MetricsBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    private static readonly Meter Meter = new("Platform.Behaviors", "1.0.0");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "request.duration",
        unit: "ms",
        description: "Request execution time in milliseconds");

    private static readonly Counter<int> TotalRequests = Meter.CreateCounter<int>(
        "request.total",
        description: "Total number of requests processed");

    private static readonly Counter<int> FailedRequests = Meter.CreateCounter<int>(
        "request.failed",
        description: "Total number of failed requests");

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestName = typeof(TRequest).Name;

        TotalRequests.Add(1);

        try
        {
            var response = await next();
            stopwatch.Stop();

            RequestDuration.Record(stopwatch.ElapsedMilliseconds, KeyValuePair.Create<string, object?>("request", requestName));

            if (IsFailure(response))
            {
                FailedRequests.Add(1, KeyValuePair.Create<string, object?>("request", requestName));
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FailedRequests.Add(1, KeyValuePair.Create<string, object?>("request", requestName));
            RequestDuration.Record(stopwatch.ElapsedMilliseconds, KeyValuePair.Create<string, object?>("request", requestName));

            logger.LogError(ex, "Request {RequestName} failed after {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static bool IsFailure(TResponse response)
    {
        if (response is null)
            return true;

        var type = response.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SharedKernel.Results.Result<>))
        {
            var isSuccess = type.GetProperty("IsSuccess");
            return isSuccess is not null && !(bool)isSuccess.GetValue(response)!;
        }

        return false;
    }
}
