using System.Text.Json;
using AuthorizationService.Application.Common.Abstractions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Contract.Events;

namespace AuthorizationService.Infrastructure.Messaging.Consumers;

public sealed class OpaSyncConsumer(
    IOpaDataUpdater opaUpdater,
    IEventConsumerDeduplicationGuard deduplicationGuard,
    ILogger<OpaSyncConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    private static readonly HashSet<string> StructuralEvents =
    [
        "authorization.role-created.v1",
        "authorization.role-activated.v1",
        "authorization.role-deactivated.v1",
        "authorization.role-disabled.v1",
        "authorization.role-parent-changed.v1",
        "authorization.role-assigned.v1",
        "authorization.role-revoked.v1",
        "authorization.permission-granted.v1",
        "authorization.permission-revoked.v1"
    ];

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (!StructuralEvents.Contains(envelope.EventType))
            return;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(OpaSyncConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
        {
            logger.LogInformation("Skipped duplicate event {EventId} for {Consumer}", envelope.EventId, nameof(OpaSyncConsumer));
            return;
        }

        try
        {
            var documentPath = ResolveDocumentPath(envelope.EventType);
            var tenantPath = $"{envelope.TenantId:N}/{documentPath}";

            var payloadData = envelope.Payload.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<object>(envelope.Payload.GetRawText())
                : new { raw = envelope.Payload.GetRawText() };

            await opaUpdater.SyncPolicyDataAsync(tenantPath, payloadData!, context.CancellationToken);

            logger.LogInformation(
                "[OPA SYNC] Synced {EventType} → {DocumentPath} for tenant {TenantId}",
                envelope.EventType, tenantPath, envelope.TenantId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "OPA sync failed for event {EventType} correlation {CorrelationId}",
                envelope.EventType, envelope.CorrelationId);
            throw;
        }
    }

    private static string ResolveDocumentPath(string eventType) => eventType switch
    {
        "authorization.role-created.v1" => "roles",
        "authorization.role-activated.v1" => "roles",
        "authorization.role-deactivated.v1" => "roles",
        "authorization.role-disabled.v1" => "roles",
        "authorization.role-parent-changed.v1" => "roles",
        "authorization.role-assigned.v1" => "assignments",
        "authorization.role-revoked.v1" => "assignments",
        "authorization.permission-granted.v1" => "permissions",
        "authorization.permission-revoked.v1" => "permissions",
        _ => "unknown"
    };
}
