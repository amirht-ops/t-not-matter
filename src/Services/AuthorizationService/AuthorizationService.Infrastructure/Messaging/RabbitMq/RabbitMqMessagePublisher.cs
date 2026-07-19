using MassTransit;
using Microsoft.Extensions.Logging;
using SharedKernel.Contract.Events;
using SharedKernel.Infrastructure.Messaging;

namespace AuthorizationService.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqMessagePublisher(
    IBus bus,
    ILogger<RabbitMqMessagePublisher> logger) : IMessagePublisher
{
    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        await bus.Publish(envelope, context =>
        {
            context.CorrelationId = envelope.CorrelationId;
            context.Headers.Set("tenant_id", envelope.TenantId.ToString("N"));
            if (envelope.CausationId.HasValue)
            {
                context.Headers.Set("causation_id", envelope.CausationId.Value.ToString("N"));
            }
        }, cancellationToken);

        logger.LogInformation(
            "Published integration event {EventType} for tenant {TenantId} and correlation {CorrelationId}",
            envelope.EventType,
            envelope.TenantId,
            envelope.CorrelationId);
    }
}
