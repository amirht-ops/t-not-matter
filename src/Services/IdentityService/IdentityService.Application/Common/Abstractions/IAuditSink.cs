namespace IdentityService.Application.Common.Abstractions;

public interface IAuditSink
{
    Task RecordAsync(string eventName, Guid tenantId, Guid correlationId, Guid? userId, bool success, string? reason, CancellationToken cancellationToken);
}
