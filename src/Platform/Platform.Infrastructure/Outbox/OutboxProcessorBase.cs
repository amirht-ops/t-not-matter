using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Contract.Events;
using SharedKernel.Infrastructure.Messaging;
using SharedKernel.Infrastructure.Outbox;

namespace Platform.Infrastructure.Outbox;

public abstract class OutboxProcessorBase<TDbContext>(
    IServiceScopeFactory scopeFactory,
    ILogger logger,
    IOptions<OutboxPublishPolicy> policy)
    : BackgroundService
    where TDbContext : DbContext
{
    private readonly OutboxPublishPolicy _policy = policy.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(_policy.PollIntervalMilliseconds);
        var leaseDuration = TimeSpan.FromSeconds(_policy.LeaseDurationSeconds);

        logger.LogInformation(
            "Outbox Processor starting. Poll: {Poll}ms, Lease: {Lease}s, Batch: {Batch}, MaxRetry: {Max}",
            _policy.PollIntervalMilliseconds, _policy.LeaseDurationSeconds, _policy.BatchSize, _policy.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPlatformOutboxRepository<TDbContext>>();
                
                var messages = await repository.ClaimPendingBatchAsync(_policy.BatchSize, leaseDuration, stoppingToken);
                if (messages.Count == 0)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                logger.LogDebug("Processing {Count} outbox messages.", messages.Count);

                foreach (var message in messages)
                {
                    await ProcessMessageAsync(scope, message, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox processing failed. Delaying before next run.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
        
        logger.LogInformation("Outbox Processor stopped.");
    }

    private async Task ProcessMessageAsync(IServiceScope scope, OutboxMessage message, CancellationToken ct)
    {
        var repository = scope.ServiceProvider.GetRequiredService<IPlatformOutboxRepository<TDbContext>>();
        var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

        try
        {
            if (message.RetryCount >= _policy.MaxAttempts)
            {
                await MoveToDeadLetterAsync(scope, message, ct);
                return;
            }

            using var payloadDocument = JsonDocument.Parse(message.Payload);
            var envelope = new EventEnvelope(
                message.Id, message.TenantId, message.CorrelationId,
                message.EventType, message.Version, message.CreatedAt,
                payloadDocument.RootElement.Clone(), message.CausationId);

            await publisher.PublishAsync(envelope, ct);
            await repository.MarkProcessedAsync(message.Id, ct);
            
            logger.LogInformation(
                "Published integration event {EventType} for tenant {TenantId} with correlation {CorrelationId}",
                message.EventType, message.TenantId, message.CorrelationId);
        }
        catch (Exception ex)
        {
            await repository.MarkFailedAsync(message.Id, ex.Message, ct);
            logger.LogError(ex,
                "Failed to publish outbox message {Id} for tenant {TenantId}.",
                message.Id, message.TenantId);
        }
    }

    private async Task MoveToDeadLetterAsync(IServiceScope scope, OutboxMessage message, CancellationToken ct)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        
        var exists = await dbContext.Set<DeadLetterMessage>()
            .AnyAsync(dl => dl.OutboxMessageId == message.Id, ct);

        if (!exists)
        {
            dbContext.Set<DeadLetterMessage>().Add(new DeadLetterMessage
            {
                Id = Guid.NewGuid(),
                OutboxMessageId = message.Id,
                EventType = message.EventType,
                Payload = message.Payload,
                Error = message.Error ?? $"Exceeded max retry attempts ({_policy.MaxAttempts})",
                TenantId = message.TenantId,
                CorrelationId = message.CorrelationId,
                MovedAt = DateTimeOffset.UtcNow
            });
        }

        message.MarkProcessed();
        await dbContext.SaveChangesAsync(ct);

        logger.LogWarning(
            "Outbox message {OutboxMessageId} exceeded max attempts -> dead letter.",
            message.Id);
    }
}
