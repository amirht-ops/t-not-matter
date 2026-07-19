namespace Platform.Middleware;

public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string RequestIdHeader = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveGuid(context.Request.Headers[CorrelationHeader].FirstOrDefault()) ?? Guid.NewGuid();
        var requestId = ResolveGuid(context.Request.Headers[RequestIdHeader].FirstOrDefault()) ?? Guid.NewGuid();

        context.Items["CorrelationId"] = correlationId;
        context.Items["RequestId"] = requestId;

        await next(context);

        if (!context.Response.HasStarted)
        {
            context.Response.Headers[CorrelationHeader] = correlationId.ToString();
            context.Response.Headers[RequestIdHeader] = requestId.ToString();
        }
    }

    private static Guid? ResolveGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
