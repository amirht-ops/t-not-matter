using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;
using SharedKernel.Errors;

namespace Platform.Behaviors;

public sealed class TenantBehavior<TRequest, TResponse>(IRequestContextAccessor contextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IResolveTenantInternally)
        {
            return await next();
        }

        var context = contextAccessor.Context;

        if (context.CanBypassTenantIsolation)
        {
            return await next();
        }

        if (context.TenantId == Guid.Empty)
        {
            if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
            {
                var resultType = typeof(TResponse).GetGenericArguments()[0];
                var failureMethod = typeof(Result<>).MakeGenericType(resultType)
                    .GetMethod("Failure", [typeof(Error)]);
                return (TResponse)failureMethod!.Invoke(null,
                    [Error.Validation("TenantMissing", "Tenant ID is required")])!;
            }
        }

        return await next();
    }
}