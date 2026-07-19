using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Platform.Abstractions.Tenant;
using SharedKernel.Authorization;
using SharedKernel.Results;
using SharedKernel.Errors;

namespace Platform.Behaviors;

public sealed class AuthorizationBehavior<TRequest, TResponse>(
    IServiceProvider serviceProvider,
    IRequestContextAccessor contextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IAuthorizableRequest authorizable)
        {
            // Service principals and platform administrators are trusted workflow orchestrators.
            if (contextAccessor.Context.IsService || contextAccessor.Context.CanBypassTenantIsolation)
                return await next();

            var authService = serviceProvider.GetService<IAuthorizationDecisionService>();
            if (authService is null)
                return await next();

            var context = contextAccessor.Context;

            var authRequest = new AuthorizationRequest(
                context.TenantId,
                context.UserId,
                authorizable.Action,
                authorizable.Resource,
                context.CorrelationId,
                context.RequestId);

            var decision = await authService.DecideAsync(authRequest, ct);

            if (!decision.IsAllowed)
            {
                if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
                {
                    var resultType = typeof(TResponse).GetGenericArguments()[0];
                    var failureMethod = typeof(Result<>).MakeGenericType(resultType)
                        .GetMethod("Failure", [typeof(Error)]);
                    return (TResponse)failureMethod!.Invoke(null,
                        [Error.Forbidden("AuthorizationDenied", decision.Reason ?? "Access denied")])!;
                }

                throw new UnauthorizedAccessException(
                    $"Authorization denied for {request.GetType().Name}: {decision.Reason ?? "Access denied"}");
            }
        }

        return await next();
    }
}
