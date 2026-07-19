using AuthorizationService.Domain.Services;
using SharedKernel.Domain.Primitives;

namespace AuthorizationService.Domain.ValueObjects;

public sealed class QuotaSnapshot : ValueObject
{
    private QuotaSnapshot(long dailyRemaining, long weeklyRemaining, long monthlyRemaining)
    {
        DailyRemaining = Math.Max(0, dailyRemaining);
        WeeklyRemaining = Math.Max(0, weeklyRemaining);
        MonthlyRemaining = monthlyRemaining;
    }

    public long DailyRemaining { get; }
    public long WeeklyRemaining { get; }
    public long MonthlyRemaining { get; }

    public static QuotaSnapshot Create(long dailyRemaining, long weeklyRemaining, long monthlyRemaining)
        => new(dailyRemaining, weeklyRemaining, monthlyRemaining);

    public static QuotaSnapshot ForNewWindow(long dailyLimit, long weeklyLimit, long monthlyLimit, long previousMonthlyRemaining = 0)
        => new(dailyLimit, weeklyLimit, ApplyMonthlyReset(monthlyLimit, previousMonthlyRemaining));

    public bool CanStartRequest() => !DailyExhausted() && !WeeklyExhausted() && !MonthlyExhausted();

    public bool DailyExhausted() => DailyRemaining <= 0;

    public bool WeeklyExhausted() => WeeklyRemaining <= 0;

    public bool MonthlyExhausted() => MonthlyRemaining <= 0;

    public bool HasMonthlyDebt() => MonthlyRemaining < 0;

    public long OutstandingDebt() => HasMonthlyDebt() ? -MonthlyRemaining : 0;

    public QuotaSnapshot ApplyConsumption(long resourceCount)
    {
        if (resourceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(resourceCount), "Resource count cannot be negative.");

        var dailyConsumed = Math.Min(resourceCount, DailyRemaining);
        var weeklyConsumed = Math.Min(resourceCount, WeeklyRemaining);

        var monthlyConsumed = resourceCount;
        if (resourceCount > dailyConsumed && resourceCount > weeklyConsumed)
            monthlyConsumed = Math.Max(0, resourceCount - dailyConsumed - weeklyConsumed);

        return new QuotaSnapshot(
            DailyRemaining - dailyConsumed,
            WeeklyRemaining - weeklyConsumed,
            MonthlyRemaining - monthlyConsumed);
    }

    public QuotaSnapshot ApplyMonthlyReset(long monthlyLimit)
        => new(DailyRemaining, WeeklyRemaining, ApplyMonthlyReset(monthlyLimit, MonthlyRemaining));

    public MultiConsumeResult ToConsumeResult()
        => new(DailyRemaining, WeeklyRemaining, MonthlyRemaining, OutstandingDebt());

    public static long ApplyMonthlyReset(long monthlyLimit, long previousMonthlyRemaining)
    {
        var outstandingDebt = previousMonthlyRemaining < 0 ? -previousMonthlyRemaining : 0;
        return monthlyLimit - outstandingDebt;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DailyRemaining;
        yield return WeeklyRemaining;
        yield return MonthlyRemaining;
    }
}
