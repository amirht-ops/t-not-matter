using System.Text.Json;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Events;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using SharedKernel.Contract.Events;

namespace AuthorizationService.Infrastructure.Caching;

public sealed class CacheInvalidationConsumer(IAuthorizationCache cache, IRoleAssignmentRepository roleAssignmentRepository, ILogger<CacheInvalidationConsumer> logger)
{
    public async Task HandleRoleAssignedAsync(RoleAssignedDomainEvent @event, CancellationToken cancellationToken)
    {
        var tenantId = @event.TenantId;
        var subjectId = SubjectId.From(@event.SubjectIdValue);
        await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        logger.LogInformation("Cache invalidated on RoleAssigned subject={SubjectId} tenant={TenantId}", @event.SubjectIdValue, @event.TenantId);
    }

    public async Task HandleRoleRevokedAsync(RoleRevokedDomainEvent @event, CancellationToken cancellationToken)
    {
        var tenantId = @event.TenantId;
        var subjectId = SubjectId.From(@event.SubjectIdValue);
        await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        logger.LogInformation("Cache invalidated on RoleRevoked subject={SubjectId} tenant={TenantId}", @event.SubjectIdValue, @event.TenantId);
    }

    public async Task HandlePermissionGrantedAsync(PermissionGrantedDomainEvent @event, CancellationToken cancellationToken)
    {
        var tenantId = @event.TenantId;
        var subjectIds = await GetSubjectIdsByRoleAsync(tenantId, @event.RoleId, cancellationToken);
        foreach (var subjectId in subjectIds)
        {
            await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        }
        logger.LogInformation("Cache invalidated on PermissionGranted role={RoleId} tenant={TenantId}", @event.RoleId, @event.TenantId);
    }

    public async Task HandlePermissionRevokedAsync(PermissionRevokedDomainEvent @event, CancellationToken cancellationToken)
    {
        var tenantId = @event.TenantId;
        var subjectIds = await GetSubjectIdsByRoleAsync(tenantId, @event.RoleId, cancellationToken);
        foreach (var subjectId in subjectIds)
        {
            await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        }
        logger.LogInformation("Cache invalidated on PermissionRevoked role={RoleId} tenant={TenantId}", @event.RoleId, @event.TenantId);
    }

    public async Task HandleUserEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var tenantId = envelope.TenantId;
        Guid? subjectId = null;

        try
        {
            subjectId = envelope.Payload.ValueKind == JsonValueKind.Object
                ? ExtractUserId(envelope.Payload)
                : null;
        }
        catch (JsonException)
        {
            subjectId = null;
        }

        if (subjectId.HasValue)
        {
            await cache.InvalidateSubjectAsync(tenantId, SubjectId.From(subjectId.Value), cancellationToken);
            logger.LogInformation(
                "Cache invalidated by {EventType} for subject={SubjectId} tenant={TenantId}",
                envelope.EventType, subjectId.Value, envelope.TenantId);
        }
        else
        {
            await cache.InvalidateTenantAsync(tenantId, cancellationToken);
            logger.LogInformation(
                "Cache invalidated by {EventType} for entire tenant {TenantId} (no subject extracted)",
                envelope.EventType, envelope.TenantId);
        }
    }

    public async Task HandleRoleAssignedEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var subjectId = ExtractPayloadGuid(envelope.Payload, "SubjectIdValue");
        if (subjectId.HasValue)
        {
            await cache.InvalidateSubjectAsync(envelope.TenantId, SubjectId.From(subjectId.Value), cancellationToken);
            logger.LogInformation("Cache invalidated on RoleAssigned subject={SubjectId} tenant={TenantId}", subjectId.Value, envelope.TenantId);
        }
    }

    public async Task HandleRoleRevokedEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var subjectId = ExtractPayloadGuid(envelope.Payload, "SubjectIdValue");
        if (subjectId.HasValue)
        {
            await cache.InvalidateSubjectAsync(envelope.TenantId, SubjectId.From(subjectId.Value), cancellationToken);
            logger.LogInformation("Cache invalidated on RoleRevoked subject={SubjectId} tenant={TenantId}", subjectId.Value, envelope.TenantId);
        }
    }

    public async Task HandlePermissionGrantedEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var roleId = ExtractPayloadGuid(envelope.Payload, "RoleId");
        if (roleId.HasValue)
        {
            var subjectIds = await GetSubjectIdsByRoleAsync(envelope.TenantId, roleId.Value, cancellationToken);
            foreach (var subjectId in subjectIds)
            {
                await cache.InvalidateSubjectAsync(envelope.TenantId, subjectId, cancellationToken);
            }
            logger.LogInformation("Cache invalidated on PermissionGranted role={RoleId} tenant={TenantId}", roleId.Value, envelope.TenantId);
        }
    }

    public async Task HandlePermissionRevokedEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var roleId = ExtractPayloadGuid(envelope.Payload, "RoleId");
        if (roleId.HasValue)
        {
            var subjectIds = await GetSubjectIdsByRoleAsync(envelope.TenantId, roleId.Value, cancellationToken);
            foreach (var subjectId in subjectIds)
            {
                await cache.InvalidateSubjectAsync(envelope.TenantId, subjectId, cancellationToken);
            }
            logger.LogInformation("Cache invalidated on PermissionRevoked role={RoleId} tenant={TenantId}", roleId.Value, envelope.TenantId);
        }
    }

    public async Task HandleRoleDisabledEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var roleId = ExtractPayloadGuid(envelope.Payload, "RoleId");
        if (roleId.HasValue)
        {
            var subjectIds = await GetSubjectIdsByRoleAsync(envelope.TenantId, roleId.Value, cancellationToken);
            foreach (var subjectId in subjectIds)
            {
                await cache.InvalidateSubjectAsync(envelope.TenantId, subjectId, cancellationToken);
            }
            logger.LogInformation("Cache invalidated on RoleDisabled role={RoleId} tenant={TenantId}", roleId.Value, envelope.TenantId);
        }
    }

    private static Guid? ExtractUserId(JsonElement payload)
    {
        if (payload.TryGetProperty("UserId", out var userIdProp) && userIdProp.ValueKind == JsonValueKind.String)
        {
            if (Guid.TryParse(userIdProp.GetString(), out var userId))
                return userId;
        }
        if (payload.TryGetProperty("userId", out var lowerUserIdProp) && lowerUserIdProp.ValueKind == JsonValueKind.String)
        {
            if (Guid.TryParse(lowerUserIdProp.GetString(), out var userId))
                return userId;
        }
        return null;
    }

    private static Guid? ExtractPayloadGuid(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String &&
            Guid.TryParse(prop.GetString(), out var value))
        {
            return value;
        }
        return null;
    }

    private async Task<IReadOnlyCollection<SubjectId>> GetSubjectIdsByRoleAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken) => await roleAssignmentRepository.GetSubjectIdsByRoleAsync(tenantId, roleId, cancellationToken: cancellationToken);
}
