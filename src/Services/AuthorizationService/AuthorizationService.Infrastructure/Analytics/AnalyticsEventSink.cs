using AuthorizationService.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace AuthorizationService.Infrastructure.Analytics;

public sealed class AnalyticsEventSink(
    ILogger<AnalyticsEventSink> logger) : IAnalyticsEventSink
{
    public Task PushEventAsync(
        string eventType,
        Guid tenantId,
        Guid correlationId,
        Guid? causationId,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[ANALYTICS] Event {EventType} | Tenant={TenantId} | Correlation={CorrelationId} | Causation={CausationId}",
            eventType, tenantId, correlationId, causationId);
        return Task.CompletedTask;
    }
}
