using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class StartupTaskRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StartupCoordinator _coordinator;
    private readonly ILogger<StartupTaskRunner> _logger;
    private readonly IOptions<StartupTaskOptions> _options;

    public StartupTaskRunner(
        IServiceScopeFactory scopeFactory,
        StartupCoordinator coordinator,
        ILogger<StartupTaskRunner> logger,
        IOptions<StartupTaskOptions> options)
    {
        _scopeFactory = scopeFactory;
        _coordinator = coordinator;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _coordinator.RecordTimeline("Service starting");
        _logger.LogInformation("Starting startup task runner");

        using var scope = _scopeFactory.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IEnumerable<IStartupTask>>();

        var taskList = tasks.ToList();
        _logger.LogInformation("Found {Count} startup task(s)", taskList.Count);

        var orderedGroups = taskList
            .GroupBy(t => t.Order)
            .OrderBy(g => g.Key);

        foreach (var group in orderedGroups)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            var executionTasks = group.Select(task =>
                ExecuteTaskWithPolicyAsync(task, stoppingToken));

            await Task.WhenAll(executionTasks);
        }

        _coordinator.MarkReady();
        _coordinator.RecordTimeline("Service ready");
    }

    private async Task ExecuteTaskWithPolicyAsync(IStartupTask task, CancellationToken stoppingToken)
    {
        var displayName = task.DisplayName;
        var policy = ResolvePolicy(displayName);

        _coordinator.RegisterTask(displayName);

        for (int attempt = 0; attempt <= policy.MaxRetries; attempt++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                _coordinator.FailTask(displayName, new OperationCanceledException("Service shutting down"));
                return;
            }

            try
            {
                using var timeoutCts = new CancellationTokenSource(policy.Timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                _coordinator.BeginTask(displayName);
                _coordinator.RecordTimeline($"{displayName} started", displayName);
                await task.ExecuteAsync(linkedCts.Token);
                _coordinator.CompleteTask(displayName);
                _coordinator.RecordTimeline($"{displayName} completed", displayName);
                return;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                var message = $"Startup task '{displayName}' timed out after {policy.Timeout}";
                if (attempt < policy.MaxRetries && policy.IsTransient(new TimeoutException(message)))
                {
                    var delay = policy.GetRetryDelay(attempt);
                    _logger.LogWarning(
                        "Startup task '{Task}' timed out (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms",
                        displayName, attempt + 1, policy.MaxRetries + 1, delay.TotalMilliseconds);
                    await Task.Delay(delay, stoppingToken);
                    continue;
                }
                _coordinator.FailTask(displayName, new TimeoutException(message));
                _coordinator.RecordTimeline($"{displayName} failed (timeout)", displayName);
                _logger.LogError("Startup task '{Task}' failed after {Attempts} attempt(s): timeout", displayName, attempt + 1);
                return;
            }
            catch (Exception ex) when (attempt < policy.MaxRetries && policy.IsTransient(ex))
            {
                var delay = policy.GetRetryDelay(attempt);
                _logger.LogWarning(ex,
                    "Startup task '{Task}' failed (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms",
                    displayName, attempt + 1, policy.MaxRetries + 1, delay.TotalMilliseconds);
                await Task.Delay(delay, stoppingToken);
                continue;
            }
            catch (Exception ex)
            {
                _coordinator.FailTask(displayName, ex);
                _coordinator.RecordTimeline($"{displayName} failed", displayName);
                _logger.LogError(ex, "Startup task '{Task}' failed after {Attempts} attempt(s)", displayName, attempt + 1);
                return;
            }
        }
    }

    private StartupTaskPolicy ResolvePolicy(string displayName)
    {
        var configured = _options.Value.Tasks;

        if (configured is not null && configured.TryGetValue(displayName, out var policy))
            return policy;

        if (StartupTaskDefaults.DefaultPolicies.TryGetValue(displayName, out var defaultPolicy))
            return defaultPolicy;

        return new StartupTaskPolicy();
    }
}
