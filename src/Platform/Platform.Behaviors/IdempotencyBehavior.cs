using MediatR;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Application;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace Platform.Behaviors;

public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyStore idempotencyStore,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentRequest idempotent)
            return await next();

        var key = idempotent.IdempotencyKey;

        var existing = await idempotencyStore.GetResponseAsync<TResponse>(key, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Idempotency hit for key {Key} on {RequestType}", key, typeof(TRequest).Name);
            return existing!;
        }

        var registered = await idempotencyStore.TryRegisterAsync(key, cancellationToken);
        if (!registered)
        {
            var retryExisting = await idempotencyStore.GetResponseAsync<TResponse>(key, cancellationToken);
            if (retryExisting is not null)
                return retryExisting!;

            logger.LogWarning("Idempotency in-progress for key {Key} on {RequestType}", key, typeof(TRequest).Name);
            return CreateInProgressResponse(key);
        }

        var response = await next();

        if (response is not null)
            await idempotencyStore.StoreResponseAsync(key, response, cancellationToken);

        return response!;
    }

    private static TResponse CreateInProgressResponse(string key)
    {
        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var error = Error.Conflict(
                "Idempotency.InProgress",
                $"A request with idempotency key '{key}' is already in progress.");
            var failure = responseType.GetMethod(nameof(Result<object>.Failure))!.Invoke(null, [error]);
            return (TResponse)failure!;
        }

        throw new InvalidOperationException($"A request with idempotency key '{key}' is already in progress.");
    }
}
