using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SharedKernel.Responses;

namespace Platform.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items["CorrelationId"] is Guid cid ? cid : Guid.Empty;
            var tenantId = context.Items["TenantId"] is Guid tid ? tid : (Guid?)null;

            logger.LogError(ex,
                "Unhandled exception {ExceptionType} at {RequestMethod} {RequestPath} " +
                "with CorrelationId {CorrelationId} and TenantId {TenantId}",
                ex.GetType().Name,
                context.Request.Method,
                context.Request.Path,
                correlationId,
                tenantId);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
            return;

        var (statusCode, code, message) = exception switch
        {
            // Minimal-API model binding / body deserialization failures. ASP.NET Core surfaces
            // malformed JSON, missing body, wrong content-type and oversized payloads as
            // BadHttpRequestException (which carries the intended status code, e.g. 400 or 415).
            // Mapping these to the generic 500 arm leaks a server-error status for a client-error
            // condition, so they are handled explicitly here.
            BadHttpRequestException bad => ((HttpStatusCode)bad.StatusCode, "BadRequest", "The request could not be processed. Verify the request body and content type."),
            // System.Text.Json parse failures and numeric conversions that overflow the target
            // type are caller mistakes, not server faults -> 400, without echoing internals.
            JsonException => (HttpStatusCode.BadRequest, "BadRequest", "The request body is not valid JSON."),
            OverflowException => (HttpStatusCode.BadRequest, "BadRequest", "A numeric value in the request is out of range."),
            FormatException => (HttpStatusCode.BadRequest, "BadRequest", "A value in the request has an invalid format."),
            ArgumentException => (HttpStatusCode.BadRequest, "BadRequest", exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized", "Authentication required"),
            KeyNotFoundException => (HttpStatusCode.NotFound, "NotFound", exception.Message),
            _ => (HttpStatusCode.InternalServerError, "InternalServerError", "An unexpected error occurred")
        };

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var correlationId = context.Items["CorrelationId"] is Guid cid ? cid : Guid.Empty;

        var problemDetails = new
        {
            type = $"https://httpstatuses.com/{(int)statusCode}",
            title = code,
            status = (int)statusCode,
            detail = message,
            traceId = System.Diagnostics.Activity.Current?.Id ?? string.Empty,
            correlationId = correlationId.ToString()
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
    }
}
