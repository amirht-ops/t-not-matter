using System.Text.RegularExpressions;
using AuthorizationService.Domain.Services;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AuthorizationService.Infrastructure.Scheduling;

public sealed partial class MonthlyQuotaResetJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MonthlyDebtRetention = TimeSpan.FromDays(31);
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<MonthlyQuotaResetJob> _logger;

    public MonthlyQuotaResetJob(IConnectionMultiplexer multiplexer, ILogger<MonthlyQuotaResetJob> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MonthlyQuotaResetJob started. Delaying first run by {Delay}s.", InitialDelay.TotalSeconds);

        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessMonthlyResetsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyQuotaResetJob failed. Will retry.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("MonthlyQuotaResetJob stopped.");
    }

    private async Task ProcessMonthlyResetsAsync(CancellationToken cancellationToken)
    {
        var db = _multiplexer.GetDatabase();
        var currentMonthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var nextMonthStart = currentMonthStart.AddMonths(1);

        var nextCursor = (RedisValue)0;
        var pattern = "quota:*:monthly:*";

        do
        {
            var result = await db.ExecuteAsync("SCAN", nextCursor, "MATCH", pattern, "COUNT", 100);
            var innerResult = (RedisResult[]?)result;
            if (innerResult is null || innerResult.Length < 2)
                break;

            nextCursor = (RedisValue)innerResult[0];
            var keys = (RedisResult[]?)innerResult[1];
            if (keys is null)
                continue;

            foreach (var key in keys)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var keyStr = (string?)key;
                if (keyStr is null)
                    continue;

                await TryResetMonthlyKeyAsync(db, keyStr, currentMonthStart, nextMonthStart);
            }
        } while (!nextCursor.IsNullOrEmpty && (long)nextCursor != 0);
    }

    private async Task TryResetMonthlyKeyAsync(IDatabase db, string key, DateTimeOffset currentMonthStart, DateTimeOffset nextMonthStart)
    {
        var dateMatch = MonthlyKeyDateRegex().Match(key);
        if (!dateMatch.Success)
            return;

        var keyDate = DateOnly.ParseExact(dateMatch.Groups[1].Value, "yyyy-MM-dd");
        var keyMonthStart = new DateTimeOffset(keyDate.Year, keyDate.Month, 1, 0, 0, 0, TimeSpan.Zero);

        if (keyMonthStart >= currentMonthStart)
            return;

        var newMonthKey = Regex.Replace(key, $":{keyDate:yyyy-MM-dd}$", $":{currentMonthStart:yyyy-MM-dd}");

        var alreadyExists = await db.KeyExistsAsync(newMonthKey);
        if (alreadyExists)
            return;

        var oldValue = await db.StringGetAsync(key);
        var limitValue = await db.StringGetAsync($"{key}:limit");

        if (oldValue.IsNullOrEmpty || limitValue.IsNullOrEmpty)
            return;

        var oldRemaining = (long)oldValue;
        var monthlyLimit = (long)limitValue;

        var quota = QuotaSnapshot.Create(0, 0, oldRemaining);
        var resetQuota = quota.ApplyMonthlyReset(monthlyLimit);
        var debt = quota.OutstandingDebt();
        var newRemaining = resetQuota.MonthlyRemaining;

        var ttlMs = (long)(nextMonthStart.Add(MonthlyDebtRetention) - DateTimeOffset.UtcNow).TotalMilliseconds;
        if (ttlMs <= 0) ttlMs = 31L * 24 * 60 * 60 * 1000;

        await db.StringSetAsync(newMonthKey, newRemaining, TimeSpan.FromMilliseconds(ttlMs));
        await db.StringSetAsync($"{newMonthKey}:limit", monthlyLimit, TimeSpan.FromMilliseconds(ttlMs));
        await db.KeyExpireAsync(newMonthKey, TimeSpan.FromMilliseconds(ttlMs));

        _logger.LogInformation(
            "Monthly quota reset: key={OldKey} oldRemaining={OldRemaining} limit={Limit} debt={Debt} newRemaining={NewRemaining} newKey={NewKey}",
            key, oldRemaining, monthlyLimit, debt, newRemaining, newMonthKey);
    }

    [GeneratedRegex(@"monthly:(\d{4}-\d{2}-\d{2})$")]
    private static partial Regex MonthlyKeyDateRegex();
}
