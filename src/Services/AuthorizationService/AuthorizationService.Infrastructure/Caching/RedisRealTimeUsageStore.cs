using AuthorizationService.Domain.Services;
using AuthorizationService.Domain.ValueObjects;
using StackExchange.Redis;

namespace AuthorizationService.Infrastructure.Caching;

public sealed class RedisRealTimeUsageStore(IConnectionMultiplexer multiplexer) : IRealTimeUsageStore
{
    private const long MonthlyDebtRetentionMilliseconds = 31L * 24 * 60 * 60 * 1000;

    public async Task<QuotaSnapshot> LoadQuotaSnapshotAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        long dailyLimit, long weeklyLimit, long monthlyLimit,
        CancellationToken cancellationToken)
    {
        var db = multiplexer.GetDatabase();
        var dailyKey = BuildMultiKey(tenantId, subjectId, action, resourceType, resourceId, dailyWindow, "daily");
        var weeklyKey = BuildMultiKey(tenantId, subjectId, action, resourceType, resourceId, weeklyWindow, "weekly");
        var monthlyKey = BuildMultiKey(tenantId, subjectId, action, resourceType, resourceId, monthlyWindow, "monthly");
        var previousMonthlyKey = BuildPreviousMonthlyKey(tenantId, subjectId, action, resourceType, resourceId, monthlyWindow);

        var results = await db.StringGetAsync([dailyKey, weeklyKey, monthlyKey, previousMonthlyKey]);

        var daily = results[0].IsNullOrEmpty ? dailyLimit : (long)results[0];
        var weekly = results[1].IsNullOrEmpty ? weeklyLimit : (long)results[1];
        var monthly = results[2].IsNullOrEmpty
            ? QuotaSnapshot.ApplyMonthlyReset(monthlyLimit, results[3].IsNullOrEmpty ? 0 : (long)results[3])
            : (long)results[2];

        return QuotaSnapshot.Create(daily, weekly, monthly);
    }

    public async Task SaveQuotaSnapshotAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        QuotaSnapshot snapshot,
        long monthlyLimit,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        CancellationToken cancellationToken)
    {
        var db = multiplexer.GetDatabase();
        var dailyKey = BuildMultiKey(tenantId, subjectId, action, resourceType, resourceId, dailyWindow, "daily");
        var weeklyKey = BuildMultiKey(tenantId, subjectId, action, resourceType, resourceId, weeklyWindow, "weekly");
        var monthlyKey = BuildMultiKey(tenantId, subjectId, action, resourceType, resourceId, monthlyWindow, "monthly");

        await db.StringSetAsync(dailyKey, snapshot.DailyRemaining, TimeSpan.FromMilliseconds(CalcTtl(dailyWindow)));
        await db.StringSetAsync(weeklyKey, snapshot.WeeklyRemaining, TimeSpan.FromMilliseconds(CalcTtl(weeklyWindow)));

        var monthlyTtl = TimeSpan.FromMilliseconds(CalcTtl(monthlyWindow) + MonthlyDebtRetentionMilliseconds);
        await db.StringSetAsync(monthlyKey, snapshot.MonthlyRemaining, monthlyTtl);
        await db.StringSetAsync($"{monthlyKey}:limit", monthlyLimit, monthlyTtl);
    }

    public async Task<(long Daily, long Weekly, long Monthly)> GetRemainingMultiAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        long dailyLimit, long weeklyLimit, long monthlyLimit,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadQuotaSnapshotAsync(
            tenantId, subjectId, action, resourceType, resourceId,
            dailyWindow, weeklyWindow, monthlyWindow,
            dailyLimit, weeklyLimit, monthlyLimit,
            cancellationToken);

        return (snapshot.DailyRemaining, snapshot.WeeklyRemaining, snapshot.MonthlyRemaining);
    }

    public async Task<MultiConsumeResult> MultiConsumeAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        long amount,
        long dailyLimit, long weeklyLimit, long monthlyLimit,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadQuotaSnapshotAsync(
            tenantId, subjectId, action, resourceType, resourceId,
            dailyWindow, weeklyWindow, monthlyWindow,
            dailyLimit, weeklyLimit, monthlyLimit,
            cancellationToken);

        var updatedSnapshot = snapshot.ApplyConsumption(amount);
        await SaveQuotaSnapshotAsync(
            tenantId, subjectId, action, resourceType, resourceId,
            updatedSnapshot,
            monthlyLimit,
            dailyWindow, weeklyWindow, monthlyWindow,
            cancellationToken);

        return updatedSnapshot.ToConsumeResult();
    }

    private static string BuildMultiKey(Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId, TimeWindow window, string suffix)
    {
        var hashSlot = $"{tenantId}:{subjectId.Value}:{action}:{resourceType}:{resourceId}";
        return $"quota:{{{hashSlot}}}:{suffix}:{window.Start:yyyy-MM-dd}";
    }

    private static string BuildPreviousMonthlyKey(Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId, TimeWindow monthlyWindow)
        => BuildMultiKey(tenantId, subjectId, action, resourceType, resourceId, TimeWindow.Monthly(monthlyWindow.Start.AddMonths(-1)), "monthly");

    private static long CalcTtl(TimeWindow window)
    {
        var remainingMs = (long)(window.End - DateTimeOffset.UtcNow).TotalMilliseconds;
        return remainingMs > 0 ? remainingMs : (long)window.DurationSeconds * 1000;
    }
}
