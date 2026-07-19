using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Serilog.Context;

namespace Platform.Middleware;

public sealed class SerilogEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var properties = new List<IDisposable>();

        try
        {
            if (context.Items.TryGetValue("CorrelationId", out var cidObj) && cidObj is Guid correlationId)
                properties.Add(LogContext.PushProperty("CorrelationId", correlationId));

            if (context.Items.TryGetValue("TenantId", out var tidObj) && tidObj is Guid tenantId && tenantId != Guid.Empty)
                properties.Add(LogContext.PushProperty("TenantId", tenantId));
            else
                properties.Add(LogContext.PushProperty("TenantId", (Guid?)null));

            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var sub = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? context.User.FindFirst("sub")?.Value;
                if (Guid.TryParse(sub, out var userId))
                    properties.Add(LogContext.PushProperty("UserId", userId));
            }

            var env = context.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
            properties.Add(LogContext.PushProperty("ServiceName", env?.ApplicationName ?? "Unknown"));
            properties.Add(LogContext.PushProperty("Environment", env?.EnvironmentName ?? "Production"));

            var traceId = System.Diagnostics.Activity.Current?.Id;
            if (!string.IsNullOrEmpty(traceId))
                properties.Add(LogContext.PushProperty("TraceId", traceId));

            await next(context);
        }
        finally
        {
            foreach (var prop in properties)
                prop.Dispose();
        }
    }
}
