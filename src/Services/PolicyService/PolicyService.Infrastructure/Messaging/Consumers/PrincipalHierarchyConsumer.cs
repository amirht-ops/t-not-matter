using System.Text.Json;
using MediatR;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using Platform.Abstractions.Tenant;
using PolicyService.Application.Features.PrincipalHierarchy;
using SharedKernel.Contract.Events;

namespace PolicyService.Infrastructure.Messaging.Consumers;

public sealed class PrincipalHierarchyConsumer(
    IEventConsumerDeduplicationGuard deduplicationGuard,
    IMediator mediator,
    IRequestContextAccessor requestContextAccessor,
    ILogger<PrincipalHierarchyConsumer> logger)
    : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);

    // UC-26/27/29 hydration. UserCreated/UserTenantChanged events are not yet emitted by any
    // service, so only the role-based events below feed the read model today.
    private static readonly HashSet<string> HandledEvents =
    [
        "authorization.role-created.v1",
        "authorization.role-assigned.v1",
        "authorization.role-revoked.v1"
    ];

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;
        if (!HandledEvents.Contains(envelope.EventType))
            return;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(PrincipalHierarchyConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
            return;

        // Consumers run outside an HTTP request, so establish a service request context so the
        // tenant RLS interceptor and the global tenant/soft-delete query filter resolve to the
        // event's tenant.
        requestContextAccessor.Context = new RequestContext(
            envelope.TenantId,
            envelope.CorrelationId,
            context.MessageId ?? Guid.NewGuid());

        try
        {
            var command = envelope.EventType switch
            {
                "authorization.role-created.v1" => BuildRoleCreated(envelope),
                "authorization.role-assigned.v1" => BuildRoleAssigned(envelope),
                "authorization.role-revoked.v1" => BuildRoleRevoked(envelope),
                _ => null
            };

            if (command is not null)
                await mediator.Send(command, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Principal hierarchy hydration failed for {EventType} {EventId}", envelope.EventType, envelope.EventId);
            throw;
        }
    }

    private static UpsertPrincipalEdgeCommand BuildRoleCreated(EventEnvelope envelope)
    {
        var roleId = envelope.Payload.TryGetProperty("RoleId", out var r) ? r.GetGuid() : Guid.Empty;
        return new UpsertPrincipalEdgeCommand(PrincipalHierarchyOperation.UpsertRoleNode, NodeId: roleId);
    }

    private static UpsertPrincipalEdgeCommand BuildRoleAssigned(EventEnvelope envelope)
    {
        var subjectId = envelope.Payload.TryGetProperty("SubjectIdValue", out var s) ? s.GetGuid() : Guid.Empty;
        var roleId = envelope.Payload.TryGetProperty("RoleId", out var r) ? r.GetGuid() : Guid.Empty;
        return new UpsertPrincipalEdgeCommand(PrincipalHierarchyOperation.UpsertEdge, UserId: subjectId, RoleId: roleId);
    }

    private static UpsertPrincipalEdgeCommand BuildRoleRevoked(EventEnvelope envelope)
    {
        var subjectId = envelope.Payload.TryGetProperty("SubjectIdValue", out var s) ? s.GetGuid() : Guid.Empty;
        var roleId = envelope.Payload.TryGetProperty("RoleId", out var r) ? r.GetGuid() : Guid.Empty;
        return new UpsertPrincipalEdgeCommand(PrincipalHierarchyOperation.RemoveEdge, UserId: subjectId, RoleId: roleId);
    }
}
