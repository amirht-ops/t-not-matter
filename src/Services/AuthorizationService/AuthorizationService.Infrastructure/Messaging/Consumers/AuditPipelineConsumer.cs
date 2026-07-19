using System.Text.Json;
using AuthorizationService.Application.Common.Abstractions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Contract.Events;

namespace AuthorizationService.Infrastructure.Messaging.Consumers;

public sealed class AuditPipelineConsumer(
    IAuthorizationAuditSink auditSink,
    IEventConsumerDeduplicationGuard deduplicationGuard,
    ILogger<AuditPipelineConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(AuditPipelineConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
        {
            logger.LogInformation("Skipped duplicate event {EventId} for {Consumer}", envelope.EventId, nameof(AuditPipelineConsumer));
            return;
        }

        try
        {
            var subjectId = TryExtractSubjectId(envelope.Payload);
            var details = envelope.Payload.ValueKind == JsonValueKind.Object
                ? envelope.Payload.GetRawText()
                : null;

            await auditSink.RecordStateChangeAsync(
                envelope.EventType,
                envelope.TenantId,
                envelope.CorrelationId,
                subjectId,
                succeeded: true,
                details,
                context.CancellationToken);

            logger.LogInformation(
                "[AUDIT] Recorded event {EventType} | Tenant={TenantId} | Correlation={CorrelationId}",
                envelope.EventType,
                envelope.TenantId,
                envelope.CorrelationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Audit recording failed for event {EventType} correlation {CorrelationId}",
                envelope.EventType, envelope.CorrelationId);
            throw;
        }
    }

    private static Guid? TryExtractSubjectId(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in new[] { "SubjectId", "SubjectIdValue", "UserId", "userId", "subjectId" })
        {
            if (payload.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                Guid.TryParse(prop.GetString(), out var id))
            {
                return id;
            }
        }

        return null;
    }
}
