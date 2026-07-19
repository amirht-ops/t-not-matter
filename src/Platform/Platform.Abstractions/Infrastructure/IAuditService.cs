namespace Platform.Abstractions.Infrastructure;

public interface IAuditService
{
    Task RecordAsync(string action, string resource, string? subjectId, string? tenantId, string? result, string? errorMessage = null, CancellationToken ct = default);
}
