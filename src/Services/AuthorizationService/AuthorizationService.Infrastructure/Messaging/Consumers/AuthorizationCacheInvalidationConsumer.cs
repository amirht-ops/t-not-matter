using AuthorizationService.Infrastructure.Caching;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Contract.Events;

namespace AuthorizationService.Infrastructure.Messaging.Consumers;

public sealed class AuthorizationCacheInvalidationConsumer(
    CacheInvalidationConsumer inner,
    IEventConsumerDeduplicationGuard deduplicationGuard,
    ILogger<AuthorizationCacheInvalidationConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(AuthorizationCacheInvalidationConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
        {
            logger.LogInformation("Skipped duplicate event {EventId} for {Consumer}", envelope.EventId, nameof(AuthorizationCacheInvalidationConsumer));
            return;
        }

        logger.LogInformation(
            "Cache invalidation triggered by event {EventType} for tenant {TenantId}",
            envelope.EventType,
            envelope.TenantId);

        try
        {
            switch (envelope.EventType)
            {
                case "UserRegisteredV1":
                case "UserActivatedV1":
                case "UserLockedV1":
                case "UserUnlockedV1":
                case "UserDisabledV1":
                case "UserDeletedV1":
                case "MfaEnabledV1":
                case "SessionRevokedV1":
                    await inner.HandleUserEventAsync(envelope, context.CancellationToken);
                    break;

                case "authorization.role-assigned.v1":
                    await inner.HandleRoleAssignedEventAsync(envelope, context.CancellationToken);
                    break;

                case "authorization.role-revoked.v1":
                    await inner.HandleRoleRevokedEventAsync(envelope, context.CancellationToken);
                    break;

                case "authorization.permission-granted.v1":
                    await inner.HandlePermissionGrantedEventAsync(envelope, context.CancellationToken);
                    break;

                case "authorization.permission-revoked.v1":
                    await inner.HandlePermissionRevokedEventAsync(envelope, context.CancellationToken);
                    break;

                case "authorization.role-disabled.v1":
                    await inner.HandleRoleDisabledEventAsync(envelope, context.CancellationToken);
                    break;

                default:
                    logger.LogDebug("No cache invalidation handler for event type {EventType}", envelope.EventType);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Cache invalidation failed for event {EventType} correlation {CorrelationId}",
                envelope.EventType, envelope.CorrelationId);
            throw;
        }
    }
}
