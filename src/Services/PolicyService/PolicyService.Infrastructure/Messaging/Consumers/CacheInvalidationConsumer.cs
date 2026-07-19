using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Caching;
using SharedKernel.Contract.Events;

namespace PolicyService.Infrastructure.Messaging.Consumers;

/// <summary>
/// P7 — invalidates the effective-subscription / effective-quota caches when config changes
/// (policy, subscription, quota). Keys are tenant-scoped via the embedded "Tenant:&lt;id&gt;" scope,
/// so invalidation is scoped per tenant (cache report §2). These caches are low-churn + 5m TTL,
/// so broad tenant-scoped invalidation is safe.
/// </summary>
public sealed class CacheInvalidationConsumer(
    IEventConsumerDeduplicationGuard deduplicationGuard,
    IDistributedCacheService cache,
    ILogger<CacheInvalidationConsumer> logger)
    : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    private static readonly HashSet<string> HandledEvents =
    [
        "policy.policy-published.v1",
        "policy.policy-archived.v1",
        "policy.subscription-assigned.v1",
        "policy.subscription-activated.v1",
        "policy.subscription-revoked.v1",
        "policy.subscription-superseded.v1",
        "policy.quota-policy-defined.v1",
        "policy.quota-policy-amended.v1",
        "policy.quota-policy-removed.v1"
    ];

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;
        if (!HandledEvents.Contains(envelope.EventType))
            return;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(CacheInvalidationConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
            return;

        var tenantPattern = $"*Tenant:{envelope.TenantId:N}*";
        try
        {
            await cache.InvalidateByPatternAsync($"policy:effective-subscription:{tenantPattern}", context.CancellationToken);
            await cache.InvalidateByPatternAsync($"policy:effective-quota:{tenantPattern}", context.CancellationToken);
            logger.LogInformation(
                "Cache invalidated for tenant {TenantId} on {EventType}", envelope.TenantId, envelope.EventType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cache invalidation failed for {EventType} {EventId}", envelope.EventType, envelope.EventId);
            throw;
        }
    }
}
