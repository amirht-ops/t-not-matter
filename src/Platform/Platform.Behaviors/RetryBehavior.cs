using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using SharedKernel.Application;

namespace Platform.Behaviors;

public sealed class RetryBehavior<TRequest, TResponse>(
    ILogger<RetryBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IRetryableRequest retryable)
            return await next();

        var options = new RetryStrategyOptions<TResponse>
        {
            MaxRetryAttempts = retryable.MaxRetries,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(100),
            ShouldHandle = args =>
            {
                var ex = args.Outcome.Exception;
                if (ex is null)
                    return ValueTask.FromResult(false);

                var isTransient = ex switch
                {
                    TimeoutException => true,
                    HttpRequestException => true,
                    TaskCanceledException tce => !tce.CancellationToken.IsCancellationRequested,
                    _ => ex.GetType().Name == "DbUpdateConcurrencyException"
                };

                return ValueTask.FromResult(isTransient);
            },
            OnRetry = args =>
            {
                logger.LogWarning(
                    "Retry {Attempt}/{MaxRetries} for {Request} after {Delay:F0}ms — {Ex}{ExMsg}",
                    args.AttemptNumber, retryable.MaxRetries, typeof(TRequest).Name,
                    args.RetryDelay.TotalMilliseconds,
                    args.Outcome.Exception?.GetType().Name ?? "unknown",
                    args.Outcome.Exception?.Message is { } m ? $": {m}" : "");
                return default;
            }
        };

        var pipeline = new ResiliencePipelineBuilder<TResponse>()
            .AddRetry(options)
            .Build();

        return await pipeline.ExecuteAsync(
            async ct => await next(),
            cancellationToken);
    }
}
