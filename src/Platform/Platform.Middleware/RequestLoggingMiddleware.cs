using System.Diagnostics;

namespace Platform.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;
        var query = context.Request.QueryString.Value ?? string.Empty;

        logger.LogDebug("HTTP {RequestMethod} {RequestPath}{RequestQueryString} started",
            method, path, query);

        try
        {
            await next(context);
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            if (statusCode >= 400)
            {
                logger.LogWarning(
                    "HTTP {RequestMethod} {RequestPath}{RequestQueryString} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, query, statusCode, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogInformation(
                    "HTTP {RequestMethod} {RequestPath}{RequestQueryString} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, query, statusCode, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "HTTP {RequestMethod} {RequestPath}{RequestQueryString} failed after {ElapsedMs}ms",
                method, path, query, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
