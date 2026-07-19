using PolicyService.Domain.Enums;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// The operational tally of units consumed within a single (action, window) scope.
/// Independent per action (business rule). Never truncated (invariant 3).
/// Carries its <see cref="Action"/> so it is a self-describing owned row when persisted.
/// </summary>
public sealed class UsageCounter : ValueObject
{
    private UsageCounter(ActionKey action, long count, QuotaWindow window, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        Action = action;
        Count = count;
        Window = window;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
    }

    public ActionKey Action { get; }
    public long Count { get; }
    public QuotaWindow Window { get; }
    public DateTimeOffset WindowStart { get; }
    public DateTimeOffset WindowEnd { get; }

    public static Result<UsageCounter> Create(ActionKey action, long count, QuotaWindow window, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (action is null)
            return Result<UsageCounter>.Failure(PolicyErrors.ActionKeyRequired);
        if (count < 0)
            return Result<UsageCounter>.Failure(PolicyErrors.NegativeCount);
        if (windowStart >= windowEnd)
            return Result<UsageCounter>.Failure(PolicyErrors.InvalidWindowRange);

        return Result<UsageCounter>.Success(new UsageCounter(action, count, window, windowStart, windowEnd));
    }

    /// <summary>Consumption is non-incremental: one operation may add many units.</summary>
    public UsageCounter Increment(long amount) => new(Action, Count + amount, Window, WindowStart, WindowEnd);

    public bool IsExceeded(long limit) => Count > limit;

    public bool IsInWindow(DateTimeOffset time) => time >= WindowStart && time < WindowEnd;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Action;
        yield return Count;
        yield return Window;
        yield return WindowStart;
        yield return WindowEnd;
    }
}
