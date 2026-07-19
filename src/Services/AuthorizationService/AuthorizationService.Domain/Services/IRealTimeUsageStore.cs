using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Domain.Services;

public sealed record MultiConsumeResult(
    long DailyNewRemaining,
    long WeeklyNewRemaining,
    long MonthlyNewRemaining,
    long MonthlyDebt);

public interface IRealTimeUsageStore
{
    async Task<QuotaSnapshot> LoadQuotaSnapshotAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        long dailyLimit, long weeklyLimit, long monthlyLimit,
        CancellationToken cancellationToken)
    {
        var remaining = await GetRemainingMultiAsync(
            tenantId, subjectId, action, resourceType, resourceId,
            dailyWindow, weeklyWindow, monthlyWindow,
            dailyLimit, weeklyLimit, monthlyLimit,
            cancellationToken);

        return QuotaSnapshot.Create(remaining.Daily, remaining.Weekly, remaining.Monthly);
    }

    Task SaveQuotaSnapshotAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        QuotaSnapshot snapshot,
        long monthlyLimit,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("This usage store does not support direct quota snapshot persistence.");

    Task<(long Daily, long Weekly, long Monthly)> GetRemainingMultiAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        long dailyLimit, long weeklyLimit, long monthlyLimit,
        CancellationToken cancellationToken);

    Task<MultiConsumeResult> MultiConsumeAsync(
        Guid tenantId, SubjectId subjectId, string action, string resourceType, string resourceId,
        long amount,
        long dailyLimit, long weeklyLimit, long monthlyLimit,
        TimeWindow dailyWindow, TimeWindow weeklyWindow, TimeWindow monthlyWindow,
        CancellationToken cancellationToken);
}
