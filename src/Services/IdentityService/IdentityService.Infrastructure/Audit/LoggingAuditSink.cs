using IdentityService.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace IdentityService.Infrastructure.Audit;

public sealed class LoggingAuditSink(ILogger<LoggingAuditSink> logger) : IAuditSink
{
    public Task RecordAsync(string eventName, Guid tenantId, Guid correlationId, Guid? userId, bool success,
        string? reason, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Audit {EventName} TenantId={TenantId} CorrelationId={CorrelationId} UserId={UserId} Success={Success} Reason={Reason}",
            eventName, tenantId, correlationId, userId, success, reason);
        return Task.CompletedTask;
    }
}
