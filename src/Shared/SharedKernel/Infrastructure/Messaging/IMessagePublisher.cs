using SharedKernel.Contract.Events;

namespace SharedKernel.Infrastructure.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}