namespace SharedKernel.Authorization;

public sealed record AuthorizationRequest(
    Guid TenantId,
    Guid? SubjectId,
    string Action,
    string Resource,
    Guid CorrelationId,
    Guid RequestId);
