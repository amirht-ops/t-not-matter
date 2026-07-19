using AuthorizationService.Domain.Errors;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace AuthorizationService.Domain.ValueObjects;

public sealed class UsageCounter : ValueObject
{
    public long Count { get; }
    public TimeWindow Window { get; }
    public string ResourceScope { get; }
    public DateTimeOffset WindowStart { get; }
    public DateTimeOffset WindowEnd { get; }

    private UsageCounter(long count, TimeWindow window, string resourceScope, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        Count = count;
        Window = window;
        ResourceScope = resourceScope;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
    }

    public static Result<UsageCounter> Create(long count, TimeWindow window, string resourceScope, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (count < 0)
            return Result<UsageCounter>.Failure(AuthorizationErrors.NegativeCount);

        if (windowStart >= windowEnd)
            return Result<UsageCounter>.Failure(AuthorizationErrors.InvalidWindowRange);

        if (string.IsNullOrWhiteSpace(resourceScope))
            return Result<UsageCounter>.Failure(AuthorizationErrors.InvalidWindowRange);

        return Result<UsageCounter>.Success(new UsageCounter(count, window, resourceScope, windowStart, windowEnd));
    }

    public UsageCounter Increment() => new(Count + 1, Window, ResourceScope, WindowStart, WindowEnd);

    public bool IsExceeded(long limit) => Count >= limit;

    public bool IsInWindow(DateTimeOffset time) => time >= WindowStart && time < WindowEnd;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Count;
        yield return Window;
        yield return ResourceScope;
        yield return WindowStart;
        yield return WindowEnd;
    }
}

public sealed class TimeWindow : ValueObject
{
    public TimeWindowType Type { get; }
    public int DurationSeconds { get; }
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    private TimeWindow(TimeWindowType type, int durationSeconds, DateTimeOffset start, DateTimeOffset end)
    {
        Type = type;
        DurationSeconds = durationSeconds;
        Start = start;
        End = end;
    }

    public static TimeWindow Daily(DateTimeOffset referenceTime)
    {
        var start = new DateTimeOffset(referenceTime.Year, referenceTime.Month, referenceTime.Day, 0, 0, 0, referenceTime.Offset);
        var end = start.AddDays(1);
        return new TimeWindow(TimeWindowType.Daily, 86400, start, end);
    }

    public static TimeWindow Weekly(DateTimeOffset referenceTime)
    {
        var dayOfWeek = (int)referenceTime.DayOfWeek;
        var start = new DateTimeOffset(referenceTime.Year, referenceTime.Month, referenceTime.Day, 0, 0, 0, referenceTime.Offset).AddDays(-dayOfWeek);
        var end = start.AddDays(7);
        return new TimeWindow(TimeWindowType.Weekly, 604800, start, end);
    }

    public static TimeWindow Monthly(DateTimeOffset referenceTime)
    {
        var start = new DateTimeOffset(referenceTime.Year, referenceTime.Month, 1, 0, 0, 0, referenceTime.Offset);
        var end = start.AddMonths(1);
        return new TimeWindow(TimeWindowType.Monthly, (int)(end - start).TotalSeconds, start, end);
    }

    public static TimeWindow Custom(TimeSpan duration, DateTimeOffset referenceTime)
    {
        var start = referenceTime;
        var end = start.Add(duration);
        return new TimeWindow(TimeWindowType.Custom, (int)duration.TotalSeconds, start, end);
    }

    public TimeWindow Next()
    {
        return Type switch
        {
            TimeWindowType.Daily => Daily(Start.AddDays(1)),
            TimeWindowType.Weekly => Weekly(Start.AddDays(7)),
            TimeWindowType.Monthly => Monthly(Start.AddMonths(1)),
            TimeWindowType.Custom => Custom(End - Start, Start.Add(End - Start)),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public bool Contains(DateTimeOffset time) => time >= Start && time < End;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return DurationSeconds;
        yield return Start;
        yield return End;
    }
}

public enum TimeWindowType
{
    Daily = 1,
    Weekly = 4,
    Monthly = 2,
    Custom = 3
}