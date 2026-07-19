using System.Reflection;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application;
using SharedKernel.Caching;
using SharedKernel.Results;

namespace Platform.Behaviors;

public sealed class CachingBehavior<TRequest, TResponse>(
    IDistributedCacheService cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    private static readonly bool IsResultType = typeof(TResponse).IsGenericType
        && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>);

    private static readonly Type? InnerValueType = IsResultType
        ? typeof(TResponse).GetGenericArguments()[0]
        : null;

    private static readonly PropertyInfo? ValueProperty = IsResultType
        ? typeof(TResponse).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)
        : null;

    private static readonly MethodInfo? CacheGetMethod = typeof(IDistributedCacheService)
        .GetMethod(nameof(IDistributedCacheService.GetAsync))!;

    private static readonly MethodInfo? SuccessFactory = IsResultType
        ? typeof(Result<>)
            .MakeGenericType(InnerValueType!)
            .GetMethod("Success", BindingFlags.Public | BindingFlags.Static)
        : null;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICachedQuery cached)
            return await next();

        if (IsResultType && InnerValueType is not null && CacheGetMethod is not null)
        {
            var cacheGetTask = (Task<object?>)CacheGetMethod
                .MakeGenericMethod(InnerValueType)
                .Invoke(cache, [cached.CacheKey, cancellationToken])!;
            var cachedValue = await cacheGetTask;
            if (cachedValue is not null)
            {
                logger.LogInformation("Cache hit for key {CacheKey} on {QueryType}", cached.CacheKey, typeof(TRequest).Name);
                return (TResponse)SuccessFactory!.Invoke(null, [cachedValue])!;
            }
        }

        var response = await next();

        if (response is not null)
        {
            var options = new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = cached.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromMinutes(5),
                FailSafe = true
            };

            if (IsResultType && ValueProperty is not null)
            {
                var value = ValueProperty.GetValue(response);
                if (value is not null)
                {
                    await cache.SetAsync(cached.CacheKey, value, options, cancellationToken);
                }
            }
            else
            {
                await cache.SetAsync(cached.CacheKey, response, options, cancellationToken);
            }

            logger.LogInformation("Cache set for key {CacheKey} on {QueryType}", cached.CacheKey, typeof(TRequest).Name);
        }

        return response!;
    }
}
