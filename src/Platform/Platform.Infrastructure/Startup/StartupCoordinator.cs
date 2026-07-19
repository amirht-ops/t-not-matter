using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class StartupCoordinator : IStartupCoordinator
{
    private readonly ConcurrentDictionary<string, StartupTaskInfo> _taskInfos = new();
    private readonly ILogger<StartupCoordinator> _logger;
    private volatile bool _isReady;
    private readonly TaskCompletionSource<bool> _readyCompletion = new();

    private DateTimeOffset? _startupStartedAt;
    private DateTimeOffset? _startupCompletedAt;
    private readonly List<StartupTimelineEntry> _timeline = [];

    public StartupCoordinator(ILogger<StartupCoordinator> logger)
    {
        _logger = logger;
    }

    public bool IsReady => _isReady;

    public IReadOnlyDictionary<string, StartupTaskInfo> TaskStatuses =>
        new Dictionary<string, StartupTaskInfo>(_taskInfos);

    public IReadOnlyList<StartupTimelineEntry> Timeline => _timeline.AsReadOnly();

    public DateTimeOffset? StartupStartedAt => _startupStartedAt;

    public DateTimeOffset? StartupCompletedAt => _startupCompletedAt;

    public TimeSpan? StartupDuration =>
        _startupStartedAt.HasValue && _startupCompletedAt.HasValue
            ? _startupCompletedAt.Value - _startupStartedAt.Value
            : null;

    public void RecordTimeline(string message, string? taskName = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (_startupStartedAt is null)
            _startupStartedAt = now;

        var entry = new StartupTimelineEntry(
            now,
            message,
            taskName,
            _startupStartedAt.HasValue ? now - _startupStartedAt.Value : null);

        lock (_timeline)
        {
            _timeline.Add(entry);
        }

        if (taskName is not null)
            _logger.LogDebug("[Timeline] +{Elapsed} - {Message}", entry.ElapsedSinceStartup, message);
        else
            _logger.LogInformation("[Timeline] +{Elapsed} - {Message}", entry.ElapsedSinceStartup, message);
    }

    public void RegisterTask(string displayName)
    {
        _taskInfos.TryAdd(displayName, StartupTaskInfo.Pending(displayName));
    }

    public void BeginTask(string displayName)
    {
        if (_taskInfos.TryGetValue(displayName, out var existing))
        {
            _taskInfos[displayName] = existing.WithStarted();
        }
        _logger.LogInformation("Startup task '{Task}' started", displayName);
    }

    public void CompleteTask(string displayName)
    {
        if (_taskInfos.TryGetValue(displayName, out var existing))
        {
            var completed = existing.WithCompleted();
            _taskInfos[displayName] = completed;
            _logger.LogInformation("Startup task '{Task}' completed ({DurationMs}ms)",
                displayName, completed.Duration?.TotalMilliseconds);
        }
    }

    public void FailTask(string displayName, Exception ex)
    {
        if (_taskInfos.TryGetValue(displayName, out var existing))
        {
            var failed = existing.WithFailed(ex);
            _taskInfos[displayName] = failed;
            _logger.LogError(ex, "Startup task '{Task}' failed after {DurationMs}ms",
                displayName, failed.Duration?.TotalMilliseconds);
        }
        else
        {
            _logger.LogError(ex, "Startup task '{Task}' failed (not registered)", displayName);
        }
    }

    public void MarkReady()
    {
        _isReady = true;
        _startupCompletedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("Service is ready (startup duration: {DurationMs}ms)",
            StartupDuration?.TotalMilliseconds);
        _readyCompletion.TrySetResult(true);
    }

    public Task WaitForReadinessAsync(CancellationToken cancellationToken)
    {
        if (_isReady)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource<bool>();
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());
        _readyCompletion.Task.ContinueWith(_ => tcs.TrySetResult(true), cancellationToken);
        return tcs.Task;
    }
}
