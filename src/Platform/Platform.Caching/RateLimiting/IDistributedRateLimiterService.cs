namespace Platform.Caching.RateLimiting;

public interface IDistributedRateLimiterService
{
    Task<bool> IsRateLimitedAsync(string policyName, string key, int permitLimit, TimeSpan window, CancellationToken cancellationToken = default);
    Task<int> GetCurrentCountAsync(string policyName, string key, CancellationToken cancellationToken = default);
    Task ResetAsync(string policyName, string key, CancellationToken cancellationToken = default);
}
