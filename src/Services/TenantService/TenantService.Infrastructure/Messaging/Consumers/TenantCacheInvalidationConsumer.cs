using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Caching;
using SharedKernel.Contract.Events;

namespace TenantService.Infrastructure.Messaging.Consumers;

public sealed class TenantCacheInvalidationConsumer(
    IDistributedCacheService cache,
    IEventConsumerDeduplicationGuard deduplicationGuard,
    ILogger<TenantCacheInvalidationConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(TenantCacheInvalidationConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
        {
            logger.LogInformation("Skipped duplicate event {EventId} for {Consumer}", envelope.EventId, nameof(TenantCacheInvalidationConsumer));
            return;
        }

        switch (envelope.EventType)
        {
            case "TenantCreatedV1":
                await HandleTenantCreated(envelope, context.CancellationToken);
                break;

            case "TenantStatusChangedV1":
            case "TenantPlanUpgradedV1":
            case "TenantNameUpdatedV1":
                await HandleTenantChanged(envelope, context.CancellationToken);
                break;

            case "DepartmentCreatedV1":
            case "DepartmentStatusChangedV1":
            case "DepartmentNameUpdatedV1":
            case "DepartmentDescriptionUpdatedV1":
                await HandleDepartmentChanged(envelope, context.CancellationToken);
                break;

            default:
                logger.LogDebug("No cache invalidation handler for event type {EventType}", envelope.EventType);
                break;
        }
    }

    private async Task HandleTenantCreated(EventEnvelope envelope, CancellationToken ct)
    {
        var slug = GetPayloadProperty(envelope.Payload, "Slug")
                   ?? GetPayloadProperty(envelope.Payload, "slug");

        if (slug is null)
        {
            logger.LogWarning(
                "TenantCreatedV1 event {EventId} for tenant {TenantId} has no slug in payload",
                envelope.EventId, envelope.TenantId);
            return;
        }

        await cache.RemoveAsync(CacheKeyBuilder.Combine("platform", "shared", "tenant", "slug", slug), ct);

        logger.LogInformation(
            "Invalidated tenant slug cache for slug={Slug} tenant={TenantId} on TenantCreatedV1", slug, envelope.TenantId);
    }

    private async Task HandleTenantChanged(EventEnvelope envelope, CancellationToken ct)
    {
        var tenantId = envelope.TenantId;
        var slug = GetPayloadProperty(envelope.Payload, "Slug")
                   ?? GetPayloadProperty(envelope.Payload, "slug");

        if (slug is not null)
        {
            await cache.RemoveAsync(CacheKeyBuilder.Combine("platform", "shared", "tenant", "slug", slug), ct);
        }

        await cache.RemoveAsync(CacheKeyBuilder.Combine("tenant-service", "tenant", "id", tenantId.ToString("N")), ct);

        logger.LogInformation(
            "Invalidated tenant cache for tenant {TenantId} on {EventType}",
            tenantId, envelope.EventType);
    }

    private async Task HandleDepartmentChanged(EventEnvelope envelope, CancellationToken ct)
    {
        var tenantId = envelope.TenantId;
        var departmentId = GetPayloadGuidProperty(envelope.Payload, "DepartmentId")
                           ?? GetPayloadGuidProperty(envelope.Payload, "departmentId")
                           ?? GetPayloadGuidProperty(envelope.Payload, "Id")
                           ?? GetPayloadGuidProperty(envelope.Payload, "id");

        if (departmentId.HasValue)
        {
            await cache.RemoveAsync($"department:id:{departmentId.Value:N}", ct);
        }

        await cache.RemoveAsync($"tenant:{tenantId:N}:departments", ct);

        logger.LogInformation(
            "Invalidated department cache for tenant {TenantId} on {EventType}",
            tenantId, envelope.EventType);
    }

    private static string? GetPayloadProperty(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        return payload.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static Guid? GetPayloadGuidProperty(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        if (payload.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            && Guid.TryParse(prop.GetString(), out var id))
        {
            return id;
        }

        return null;
    }
}
