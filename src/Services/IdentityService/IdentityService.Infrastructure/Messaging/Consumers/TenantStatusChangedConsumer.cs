using System.Text.Json;
using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Tenant;
using SharedKernel.Contract.Events;

namespace IdentityService.Infrastructure.Messaging.Consumers;

public sealed class TenantStatusChangedConsumer(
    ISessionRepository sessionRepository,
    IIdentityUnitOfWork unitOfWork,
    IRequestContextAccessor requestContextAccessor,
    ILogger<TenantStatusChangedConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly HashSet<string> InvalidStatuses = ["disabled", "suspended"];

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (envelope.EventType != "TenantStatusChangedV1")
            return;

        logger.LogInformation("Processing TenantStatusChangedV1 for tenant {TenantId}", envelope.TenantId);

        requestContextAccessor.Context = new RequestContext(envelope.TenantId, envelope.CorrelationId, envelope.EventId);

        var newStatus = GetPayloadProperty(envelope.Payload, "NewStatus");
        if (newStatus is null)
        {
            logger.LogWarning("NewStatus property missing in TenantStatusChangedV1 payload for tenant {TenantId}", envelope.TenantId);
            return;
        }

        logger.LogInformation("Tenant {TenantId} status changed to {NewStatus}", envelope.TenantId, newStatus);

        if (!InvalidStatuses.Contains(newStatus.ToLowerInvariant()))
            return;

        var sessions = await sessionRepository.GetByTenantIdAsync(envelope.TenantId, trackChanges: true, cancellationToken: context.CancellationToken);

        if (sessions.Count == 0)
        {
            logger.LogInformation("No active sessions found for tenant {TenantId}", envelope.TenantId);
            return;
        }

        logger.LogInformation("Revoking {Count} sessions for tenant {TenantId} due to invalid status", sessions.Count, envelope.TenantId);

        var correlationId = envelope.CorrelationId;

        foreach (var session in sessions)
        {
            session.Revoke(correlationId);
        }

        await unitOfWork.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Successfully revoked sessions for tenant {TenantId}", envelope.TenantId);
    }

    private static string? GetPayloadProperty(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        return payload.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
