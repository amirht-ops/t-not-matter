using Microsoft.Extensions.Logging;
using TenantService.Application.Common.Abstractions;

namespace TenantService.Infrastructure.Audit;

public sealed class LoggingAuditService(ILogger<LoggingAuditService> logger) : IAuditService
{
    public Task RecordAsync(string action, string resource, string? subjectId, string? tenantId, string? result, string? errorMessage = null, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Audit: Action={Action} Resource={Resource} SubjectId={SubjectId} TenantId={TenantId} Result={Result} Error={Error}",
            action, resource, subjectId, tenantId, result, errorMessage);
        return Task.CompletedTask;
    }
}
