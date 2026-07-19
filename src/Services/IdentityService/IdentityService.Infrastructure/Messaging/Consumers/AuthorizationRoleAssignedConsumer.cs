using System.Text.Json;
using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain;
using IdentityService.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Tenant;
using SharedKernel.Contract.Events;

namespace IdentityService.Infrastructure.Messaging.Consumers;

public sealed class AuthorizationRoleAssignedConsumer(
    IUserRepository users,
    IIdentityUnitOfWork unitOfWork,
    IRequestContextAccessor requestContextAccessor,
    ILogger<AuthorizationRoleAssignedConsumer> logger) : IConsumer<EventEnvelope>
{
    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;
        if (envelope.EventType != "authorization.role-assigned.v1")
            return;

        var subjectId = GetGuid(envelope.Payload, "SubjectIdValue");
        var roleId = GetGuid(envelope.Payload, "RoleId");
        var tenantId = GetGuid(envelope.Payload, "TenantIdValue") ?? envelope.TenantId;
        var departmentId = GetGuid(envelope.Payload, "DepartmentIdValue");

        if (subjectId is null || roleId is null || departmentId is null)
        {
            logger.LogWarning("Skipping role assigned event {EventId}; payload is missing identity synchronization fields", envelope.EventId);
            return;
        }

        requestContextAccessor.Context = new RequestContext(
            tenantId,
            envelope.CorrelationId,
            envelope.EventId,
            CanBypassTenantIsolation: true);

        var user = await users.GetByIdAcrossTenantsAsync(subjectId.Value, trackChanges: false, context.CancellationToken);
        if (user is null)
        {
            logger.LogWarning("Role assigned event {EventId} references unknown user {UserId}", envelope.EventId, subjectId);
            return;
        }

        var oldTenantId = user.TenantId;
        var syncResult = user.SynchronizeTenant(tenantId, departmentId.Value, roleId.Value, envelope.CorrelationId);
        if (syncResult.IsFailure)
        {
            logger.LogWarning("Unable to synchronize user {UserId}: {Reason}", user.Id, syncResult.Error.Description);
            return;
        }

        if (oldTenantId == user.TenantId)
            await users.UpdateAsync(user, context.CancellationToken);
        else
            await users.MoveToTenantAsync(user, oldTenantId, context.CancellationToken);

        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Synchronized identity user {UserId} from tenant {OldTenantId} to tenant {TenantId}, department {DepartmentId}, role {RoleId}",
            user.Id, oldTenantId, user.TenantId, departmentId, roleId);
    }

    private static Guid? GetGuid(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }
}
