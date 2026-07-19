namespace Platform.Abstractions.Infrastructure;

public interface IReadinessService
{
    Task<ReadinessResult> CheckReadinessAsync(CancellationToken cancellationToken = default);
}

public sealed record ReadinessResult(
    bool IsReady,
    IReadOnlyDictionary<string, ReadinessComponentStatus> Components)
{
    public static ReadinessResult Ready() =>
        new(true, new Dictionary<string, ReadinessComponentStatus>
        {
            ["Service"] = ReadinessComponentStatus.Ready
        });
}

public sealed record ReadinessComponentStatus(
    bool IsHealthy,
    string? Message = null,
    TimeSpan? Duration = null)
{
    public static readonly ReadinessComponentStatus Ready = new(true, "Ready");
    public static readonly ReadinessComponentStatus NotChecked = new(false, "Not checked");
}
