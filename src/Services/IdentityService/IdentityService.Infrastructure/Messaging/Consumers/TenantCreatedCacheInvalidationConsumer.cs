using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Caching;
using SharedKernel.Contract.Events;

namespace IdentityService.Infrastructure.Messaging.Consumers;

public sealed class TenantCreatedCacheInvalidationConsumer(
    IDistributedCacheService cache,
    IEventConsumerDeduplicationGuard deduplicationGuard,
    ILogger<TenantCreatedCacheInvalidationConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (envelope.EventType != "TenantCreatedV1")
            return;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(TenantCreatedCacheInvalidationConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
        {
            logger.LogInformation("Skipped duplicate event {EventId} for {Consumer}", envelope.EventId, nameof(TenantCreatedCacheInvalidationConsumer));
            return;
        }

        var slug = GetPayloadProperty(envelope.Payload, "Slug")
                   ?? GetPayloadProperty(envelope.Payload, "slug");

        if (slug is null)
        {
            logger.LogWarning(
                "TenantCreatedV1 event {EventId} for tenant {TenantId} has no slug in payload — cannot invalidate negative cache",
                envelope.EventId, envelope.TenantId);
            return;
        }

        var cacheKey = CacheKeyBuilder.Combine("platform", "shared", "tenant", "slug", slug);
        await cache.RemoveAsync(cacheKey, context.CancellationToken);

        logger.LogInformation(
            "Invalidated negative tenant slug cache for slug={Slug} tenant={TenantId} on TenantCreatedV1",
            slug, envelope.TenantId);
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
