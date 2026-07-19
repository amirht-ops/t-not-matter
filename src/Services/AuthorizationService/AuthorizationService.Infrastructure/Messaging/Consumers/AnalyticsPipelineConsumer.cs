using AuthorizationService.Application.Common.Abstractions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Contract.Events;

namespace AuthorizationService.Infrastructure.Messaging.Consumers;

public sealed class AnalyticsPipelineConsumer(
    IAnalyticsEventSink analyticsSink,
    IEventConsumerDeduplicationGuard deduplicationGuard,
    ILogger<AnalyticsPipelineConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(AnalyticsPipelineConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
        {
            logger.LogInformation("Skipped duplicate event {EventId} for {Consumer}", envelope.EventId, nameof(AnalyticsPipelineConsumer));
            return;
        }

        try
        {
            var payloadJson = envelope.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                ? envelope.Payload.GetRawText()
                : envelope.Payload.GetRawText();

            await analyticsSink.PushEventAsync(
                envelope.EventType,
                envelope.TenantId,
                envelope.CorrelationId,
                envelope.CausationId,
                payloadJson,
                context.CancellationToken);

            logger.LogInformation(
                "[ANALYTICS] Forwarded event {EventType} | Tenant={TenantId} | Correlation={CorrelationId}",
                envelope.EventType,
                envelope.TenantId,
                envelope.CorrelationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Analytics forwarding failed for event {EventType} correlation {CorrelationId}",
                envelope.EventType, envelope.CorrelationId);
            throw;
        }
    }
}
