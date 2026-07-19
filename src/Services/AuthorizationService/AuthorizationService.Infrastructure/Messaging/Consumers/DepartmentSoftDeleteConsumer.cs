using System.Text.Json;
using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;
using Platform.Abstractions.Tenant;
using SharedKernel.Contract.Events;

namespace AuthorizationService.Infrastructure.Messaging.Consumers;

public sealed class DepartmentSoftDeleteConsumer(
    AuthorizationDbContext dbContext,
    IEventConsumerDeduplicationGuard deduplicationGuard,
    IRequestContextAccessor requestContextAccessor,
    ILogger<DepartmentSoftDeleteConsumer> logger) : IConsumer<EventEnvelope>
{
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromDays(7);
    private static readonly string DepartmentSoftDeletedEventType = "DepartmentSoftDeletedV1";

    public async Task Consume(ConsumeContext<EventEnvelope> context)
    {
        var envelope = context.Message;

        if (envelope.EventType != DepartmentSoftDeletedEventType)
            return;

        if (!await deduplicationGuard.TryBeginProcessingAsync(nameof(DepartmentSoftDeleteConsumer), envelope.EventId, DeduplicationTtl, context.CancellationToken))
        {
            logger.LogInformation("Skipped duplicate event {EventId} for {Consumer}", envelope.EventId, nameof(DepartmentSoftDeleteConsumer));
            return;
        }

        var tenantId = envelope.TenantId;

        requestContextAccessor.Context = new RequestContext(tenantId, envelope.CorrelationId, envelope.EventId);

        var departmentId = TryExtractDepartmentId(envelope.Payload);
        if (!departmentId.HasValue)
        {
            logger.LogWarning(
                "Failed to extract departmentId from event {EventId} payload for tenant {TenantId}",
                envelope.EventId, tenantId);
            return;
        }

        try
        {
            var roles = await dbContext.Roles
                .IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId && r.DepartmentId == departmentId.Value && !r.IsDeleted && r.Status != RoleStatus.Disabled)
                .ToListAsync(context.CancellationToken);

            if (roles.Count == 0)
            {
                logger.LogInformation(
                    "No active roles found for department {DepartmentId} in tenant {TenantId}",
                    departmentId.Value, tenantId);
                return;
            }

            var correlationId = envelope.CorrelationId;
            foreach (var role in roles)
            {
                role.Disable(correlationId);
            }

            await dbContext.SaveChangesAsync(context.CancellationToken);

            logger.LogInformation(
                "Disabled {RoleCount} roles for department {DepartmentId} in tenant {TenantId} (event {EventId})",
                roles.Count, departmentId.Value, tenantId, envelope.EventId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to disable roles for department {DepartmentId} in tenant {TenantId} (event {EventId})",
                departmentId.Value, tenantId, envelope.EventId);
            throw;
        }
    }

    private static Guid? TryExtractDepartmentId(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in new[] { "DepartmentId", "departmentId", "department_id" })
        {
            if (payload.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                Guid.TryParse(prop.GetString(), out var id))
            {
                return id;
            }
        }

        return null;
    }
}
