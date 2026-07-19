namespace Platform.Abstractions.Infrastructure;

public interface IStartupCoordinator
{
    bool IsReady { get; }
    IReadOnlyDictionary<string, StartupTaskInfo> TaskStatuses { get; }
    IReadOnlyList<StartupTimelineEntry> Timeline { get; }
    DateTimeOffset? StartupStartedAt { get; }
    DateTimeOffset? StartupCompletedAt { get; }
    TimeSpan? StartupDuration { get; }
    Task WaitForReadinessAsync(CancellationToken cancellationToken);
}

public enum StartupTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public sealed record StartupTaskInfo(
    string DisplayName,
    StartupTaskStatus Status,
    DateTime? StartedAt = null,
    DateTime? FinishedAt = null,
    TimeSpan? Duration = null,
    string? Exception = null)
{
    public static StartupTaskInfo Pending(string displayName) =>
        new(displayName, StartupTaskStatus.Pending);

    public StartupTaskInfo WithStarted() =>
        this with { Status = StartupTaskStatus.Running, StartedAt = DateTime.UtcNow };

    public StartupTaskInfo WithCompleted() =>
        this with { Status = StartupTaskStatus.Completed, FinishedAt = DateTime.UtcNow, Duration = DateTime.UtcNow - StartedAt };

    public StartupTaskInfo WithFailed(Exception ex) =>
        this with { Status = StartupTaskStatus.Failed, FinishedAt = DateTime.UtcNow, Duration = DateTime.UtcNow - StartedAt, Exception = ex.Message };
}

public sealed record StartupTimelineEntry(
    DateTimeOffset Timestamp,
    string Message,
    string? TaskName = null,
    TimeSpan? ElapsedSinceStartup = null);
