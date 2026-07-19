using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Authorization;

namespace Platform.Behaviors;

public sealed class AuditBehavior<TRequest, TResponse>(
    ILogger<AuditBehavior<TRequest, TResponse>> logger,
    IEnumerable<IAuditService> auditServices)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestName = typeof(TRequest).Name;

        try
        {
            var response = await next();
            stopwatch.Stop();

            var auditServiceList = auditServices.ToList();
            if (auditServiceList.Count != 0)
            {
                var action = request is IAuthorizableRequest ar ? ar.Action : requestName;
                var resource = request is IAuthorizableRequest rr ? rr.Resource : string.Empty;

                await Task.WhenAll(auditServiceList.Select(s =>
                    s.RecordAsync(action, resource, null, null, "Success", null, ct)));
            }

            logger.LogInformation(
                "Request {RequestName} completed in {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var auditServiceList = auditServices.ToList();
            if (auditServiceList.Count != 0)
            {
                var action = request is IAuthorizableRequest ar ? ar.Action : requestName;
                var resource = request is IAuthorizableRequest rr ? rr.Resource : string.Empty;

                await Task.WhenAll(auditServiceList.Select(s =>
                    s.RecordAsync(action, resource, null, null, "Error", ex.Message, ct)));
            }

            logger.LogError(ex,
                "Request {RequestName} failed after {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
