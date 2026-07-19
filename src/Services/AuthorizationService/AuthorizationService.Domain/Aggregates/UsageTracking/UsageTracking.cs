using AuthorizationService.Domain.Errors;
using AuthorizationService.Domain.Events;
using AuthorizationService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace AuthorizationService.Domain.Aggregates.UsageTracking;

public sealed class UsageTracking : AggregateRoot
{
    private UsageTracking() { }

    private UsageTracking(Guid id, Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId, TimeWindow window, long count, DateTimeOffset windowStart, DateTimeOffset windowEnd) : base(id, tenantId)
    {
        SubjectId = subjectId;
        Action = action;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Window = window;
        Count = count;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
    }

    public SubjectId SubjectId { get; private init; }
    public string Action { get; private init; }
    public string ResourceType { get; private init; }
    public string ResourceId { get; private init; }
    public TimeWindow Window { get; private init; }
    public long Count { get; private set; }
    public DateTimeOffset WindowStart { get; private init; }
    public DateTimeOffset WindowEnd { get; private init; }
    public DateTimeOffset LastAccessedAt { get; private set; }
    public bool IsExpired => DateTimeOffset.UtcNow >= WindowEnd;

    public static UsageTracking Create(Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId, TimeWindow window, Guid correlationId)
    {
        var tracking = new UsageTracking(
            Guid.NewGuid(),
            tenantId,
            subjectId,
            action,
            resourceType,
            resourceId,
            window,
            0,
            window.Start,
            window.End);

        tracking.RaiseDomainEvent(new UsageTrackingCreatedDomainEvent(tracking.Id, tenantId, subjectId.Value, action, resourceType, resourceId, correlationId));
        return tracking;
    }

    public Result<UsageCounter> GetCounter()
    {
        return UsageCounter.Create(Count, Window, $"{ResourceType}:{ResourceId}", WindowStart, WindowEnd);
    }

    public Result<Unit> Increment(Guid correlationId)
    {
        if (IsExpired)
            return Result<Unit>.Failure(AuthorizationErrors.CannotIncrementExpiredUsage);

        Count++;
        LastAccessedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
        RaiseDomainEvent(new UsageIncrementedDomainEvent(Id, TenantId, SubjectId.Value, Action, ResourceType, ResourceId, Count, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public long RemainingQuota(long limit) => Math.Max(0, limit - Count);

    public Result<Unit> EnsureTenantMatch(Guid expectedTenantId)
    {
        if (TenantId != expectedTenantId)
            return Result<Unit>.Failure(AuthorizationErrors.UsageTrackingTenantMismatch);

        return Result<Unit>.Success(Unit.Value);
    }
}
