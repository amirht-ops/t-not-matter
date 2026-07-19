using System.Text.Json;
using MediatR;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using Platform.Abstractions.Tenant;
using PolicyService.Application.Abstractions;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Contract.Events;

namespace PolicyService.Infrastructure.Messaging.Consumers;

public sealed class OpaSyncConsumer(
    IEventConsumerDeduplicationGuard deduplicationGuard,
    IOpaDataUpdater opaUpdater,
    IPolicyRepository policies,
    IRequestContextAccessor requestContextAccessor,
    ILogger<OpaSyncConsumer> logger)
    : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    private static readonly HashSet<string> HandledEvents =
    [
        "policy.policy-published.v1",
        "policy.policy-archived.v1"
    ];

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;
        if (!HandledEvents.Contains(envelope.EventType))
            return;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(OpaSyncConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
            return;

        // The consumer loads the Policy aggregate, so establish a service request context whose
        // tenant matches the event so the RLS interceptor + global filter resolve correctly.
        requestContextAccessor.Context = new RequestContext(
            envelope.TenantId,
            envelope.CorrelationId,
            context.MessageId ?? Guid.NewGuid());

        try
        {
            if (!TryGetPolicyId(envelope, out var policyId))
            {
                logger.LogWarning("OpaSync: could not extract PolicyId from {EventType} {EventId}", envelope.EventType, envelope.EventId);
                return;
            }

            var documentPath = $"policy/{envelope.TenantId:N}/{policyId:N}";

            if (envelope.EventType == "policy.policy-published.v1")
            {
                var policy = await policies.GetByIdAsync(envelope.TenantId, PolicyId.From(policyId), trackChanges: false, cancellationToken: context.CancellationToken);
                if (policy is null || policy.CompiledRego is null)
                {
                    logger.LogWarning("OpaSync: policy {PolicyId} not found or has no compiled Rego; skipping sync", policyId);
                    return;
                }

                await opaUpdater.SyncPolicyDataAsync(documentPath, policy.CompiledRego.Source, context.CancellationToken);
                logger.LogInformation("OpaSync: published policy {PolicyId} → {DocumentPath}", policyId, documentPath);
            }
            else
            {
                // policy.policy-archived.v1: remove the document from OPA (PUT null deletes the node).
                await opaUpdater.SyncPolicyDataAsync(documentPath, null, context.CancellationToken);
                logger.LogInformation("OpaSync: removed archived policy {PolicyId} → {DocumentPath}", policyId, documentPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpaSync failed for {EventType} {EventId}", envelope.EventType, envelope.EventId);
            throw;
        }
    }

    private static bool TryGetPolicyId(EventEnvelope envelope, out Guid policyId)
    {
        policyId = Guid.Empty;
        if (!envelope.Payload.TryGetProperty("PolicyId", out var policyIdElement))
            return false;
        if (!policyIdElement.TryGetProperty("Value", out var valueElement))
            return false;
        policyId = valueElement.GetGuid();
        return policyId != Guid.Empty;
    }
}
