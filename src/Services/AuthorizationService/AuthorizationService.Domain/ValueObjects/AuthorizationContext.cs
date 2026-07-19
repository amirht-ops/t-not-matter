using SharedKernel.Domain.Guards;
using SharedKernel.Domain.Primitives;

namespace AuthorizationService.Domain.ValueObjects;

public sealed class AuthorizationContext : ValueObject
{
    private AuthorizationContext(string? ipAddress, string? userAgent, IReadOnlyDictionary<string, string> environment, IReadOnlyDictionary<string, string> usage, Guid correlationId, DateTimeOffset requestTime)
    {
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Environment = environment;
        Usage = usage;
        CorrelationId = Guard.NotEmpty(correlationId, nameof(correlationId));
        RequestTime = requestTime;
    }

    public string? IpAddress { get; }
    public string? UserAgent { get; }
    public IReadOnlyDictionary<string, string> Environment { get; }
    public IReadOnlyDictionary<string, string> Usage { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset RequestTime { get; }

    public static AuthorizationContext Create(string? ipAddress, string? userAgent, IReadOnlyDictionary<string, string>? environment, IReadOnlyDictionary<string, string>? usage, Guid correlationId, DateTimeOffset requestTime) =>
        new(ipAddress, userAgent, environment ?? new Dictionary<string, string>(), usage ?? new Dictionary<string, string>(), correlationId, requestTime);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IpAddress;
        yield return UserAgent;
        yield return CorrelationId;
        yield return RequestTime;
        foreach (var pair in Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { yield return pair.Key; yield return pair.Value; }
        foreach (var pair in Usage.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { yield return pair.Key; yield return pair.Value; }
    }
}
