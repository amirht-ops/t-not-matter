using System.Collections.Generic;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Events;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.Aggregates.UsageLedgers;

/// <summary>
/// Per-consumer, per-action operational consumption tally. High-churn; resets per window.
/// Separate aggregate from <see cref="QuotaPolicy"/> (config) and <see cref="DebtLedger"/>
/// (liability). Counters are independent per action (business rule). Consumption always
/// succeeds; usage is never truncated (invariant 3).
/// State is held as a <see cref="List{UsageCounter}"/> so it maps to a normalized owned
/// collection (one row per action/window) under EF Core.
/// </summary>
public sealed class UsageLedger : AggregateRoot
{
    private UsageLedger() { }

    private UsageLedger(Guid id, Guid tenantId, ConsumerId consumerId)
        : base(id, tenantId)
    {
        ConsumerId = consumerId;
    }

    /// <summary>Persistence- and domain-backed collection of per-(action,window) counters.</summary>
    public List<UsageCounter> Counters { get; } = [];

    public ConsumerId ConsumerId { get; private set; } = null!;

    public static Result<UsageLedger> Create(Guid tenantId, ConsumerId consumerId)
    {
        if (tenantId == Guid.Empty)
            return Result<UsageLedger>.Failure(PolicyErrors.TenantMismatch);
        if (consumerId is null)
            return Result<UsageLedger>.Failure(PolicyErrors.ConsumerIdRequired);

        return Result<UsageLedger>.Success(new UsageLedger(Guid.NewGuid(), tenantId, consumerId));
    }

    /// <summary>Records consumption for a window. Never truncated; increments the counter.</summary>
    public Result<Unit> Record(ConsumerId consumerId, ActionKey action, QuotaWindow window, long amount, DateTimeOffset now, Guid correlationId)
    {
        if (amount < 0)
            return Result<Unit>.Failure(PolicyErrors.NegativeCount);

        //مشخص کردن بازه زمانی 
        var (start, end) = GetWindowBoundaries(window, now);

        var existing = Counters.FirstOrDefault(c => c.Action == action && c.Window == window && c.IsInWindow(now));
        UsageCounter updated;
        if (existing is not null)
        {
            updated = existing.Increment(amount);
        }
        else
        {
            var created = UsageCounter.Create(action, 0, window, start, end);
            if (created.IsFailure)
                return Result<Unit>.Failure(created.Error);
            updated = created.Value!.Increment(amount);
        }

        Upsert(updated);
        MarkUpdated();
        RaiseDomainEvent(new UsageRecordedDomainEvent(UsageLedgerId.From(Id), consumerId, action, window, updated.Count, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public long GetCount(ActionKey action, QuotaWindow window, DateTimeOffset now)
    {
        var counter = Counters.FirstOrDefault(c => c.Action == action && c.Window == window && c.IsInWindow(now));
        return counter is not null ? counter.Count : 0;
    }

    /// <summary>Administrative reset clears usage only (never debt, never quota policy).</summary>
    public Result<Unit> Reset(Guid correlationId)
    {
        Counters.Clear();
        MarkUpdated();
        RaiseDomainEvent(new UsageResetDomainEvent(UsageLedgerId.From(Id), ConsumerId, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> MarkAllowed(ActionKey actionKey, Guid correlationId)
    {
        RaiseDomainEvent(new OperationAllowedDomainEvent(ConsumerId, actionKey, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> MarkDenied(ActionKey actionKey, string reason, Guid correlationId)
    {
        RaiseDomainEvent(new OperationDeniedDomainEvent(ConsumerId, actionKey, reason, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public UsageLedgerId GetUsageLedgerId() => UsageLedgerId.From(Id);

    private void Upsert(UsageCounter counter)
    {
        var index = Counters.FindIndex(c => c.Action == counter.Action && c.Window == counter.Window);
        if (index >= 0)
            Counters[index] = counter;
        else
            Counters.Add(counter);
    }

    private static (DateTimeOffset start, DateTimeOffset end) GetWindowBoundaries(QuotaWindow window, DateTimeOffset now)
        => GetWindowBoundariesPublic(window, now);

    public static (DateTimeOffset start, DateTimeOffset end) GetWindowBoundariesPublic(QuotaWindow window, DateTimeOffset now)
    {
        return window switch
        {
            QuotaWindow.Daily => DailyBoundaries(now),
            QuotaWindow.Weekly => WeeklyBoundaries(now),
            QuotaWindow.Monthly => MonthlyBoundaries(now),
            _ => throw new ArgumentOutOfRangeException(nameof(window))
        };
    }

    private static (DateTimeOffset, DateTimeOffset) DailyBoundaries(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return (start, start.AddDays(1));
    }

    private static (DateTimeOffset, DateTimeOffset) WeeklyBoundaries(DateTimeOffset now)
    {
        var diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset).AddDays(-diff);
        return (start, start.AddDays(7));
    }

    private static (DateTimeOffset, DateTimeOffset) MonthlyBoundaries(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        return (start, start.AddMonths(1));
    }
}
