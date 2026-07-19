using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace Platform.Behaviors;

public sealed class FailClosedBehavior<TRequest, TResponse>(ILogger<FailClosedBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception exception) when (request is IAuthorizableRequest)
        {
            logger.LogError(exception, "Fail-closed triggered for {RequestType} {Request}",
                typeof(TRequest).Name, request);

            if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
            {
                var failureMethod = typeof(Result<>).MakeGenericType(typeof(TResponse).GetGenericArguments()[0])
                    .GetMethod(nameof(Result<object>.Failure), new[] { typeof(Error) });

                if (failureMethod is not null)
                {
                    return (TResponse)failureMethod.Invoke(null, [GeneralErrors.ServiceUnavailable])!;
                }
            }

            throw;
        }
    }
}
