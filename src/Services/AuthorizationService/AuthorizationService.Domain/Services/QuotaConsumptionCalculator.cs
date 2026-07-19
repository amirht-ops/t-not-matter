namespace AuthorizationService.Domain.Services;

public static class QuotaConsumptionCalculator
{
    public static MultiConsumeResult Consume(
        long requestedAmount,
        long dailyRemaining,
        long weeklyRemaining,
        long monthlyRemaining)
        => ValueObjects.QuotaSnapshot
            .Create(dailyRemaining, weeklyRemaining, monthlyRemaining)
            .ApplyConsumption(requestedAmount)
            .ToConsumeResult();

    public static long ResetMonthly(long monthlyLimit, long previousMonthlyRemaining)
        => ValueObjects.QuotaSnapshot.ApplyMonthlyReset(monthlyLimit, previousMonthlyRemaining);
}
