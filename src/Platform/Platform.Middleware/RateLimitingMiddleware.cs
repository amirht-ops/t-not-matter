using System.Net;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Middleware;
using Platform.Caching.RateLimiting;

namespace Platform.Middleware;

public sealed class RateLimitingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IDistributedRateLimiterService rateLimiter,
        IOptions<RateLimitingOptions> options)
    {
        var opts = options.Value;
        var key = BuildKey(context, opts);

        if (key is not null)
        {
            var limited = await rateLimiter.IsRateLimitedAsync(
                opts.DefaultPolicyName,
                key,
                opts.DefaultPermitLimit,
                TimeSpan.FromSeconds(opts.DefaultWindowSeconds));

            if (limited)
            {
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.Headers.RetryAfter = opts.RetryAfterSeconds.ToString();
                return;
            }
        }

        await next(context);
    }

    private static string? BuildKey(HttpContext context, RateLimitingOptions opts)
    {
        if (opts.UseUserIdAsKey)
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? context.User.FindFirst("sub")?.Value;
            if (userId is not null) return $"user:{userId}";
        }

        if (opts.UseClientIpAsKey)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString();
            if (ip is not null) return $"ip:{ip}";
        }

        return null;
    }
}
