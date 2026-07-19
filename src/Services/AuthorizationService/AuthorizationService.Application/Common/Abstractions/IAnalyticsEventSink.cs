namespace AuthorizationService.Application.Common.Abstractions;

public interface IAnalyticsEventSink
{
    Task PushEventAsync(
        string eventType,
        Guid tenantId,
        Guid correlationId,
        Guid? causationId,
        string payloadJson,
        CancellationToken cancellationToken);
}
