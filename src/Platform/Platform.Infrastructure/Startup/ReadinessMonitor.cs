using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Caching;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class ReadinessMonitorOptions
{
    public int CheckIntervalSeconds { get; set; } = 5;
}

public sealed class ReadinessState
{
    private ReadinessResult? _latest;
    private readonly object _lock = new();

    public ReadinessResult Latest
    {
        get
        {
            lock (_lock) return _latest ?? ReadinessResult.Ready();
        }
        set
        {
            lock (_lock) _latest = value;
        }
    }
}

public sealed class ReadinessMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStartupCoordinator _coordinator;
    private readonly IRedisConnectionFactory? _redisFactory;
    private readonly IBus? _bus;
    private readonly ILogger<ReadinessMonitor> _logger;
    private readonly ReadinessState _state;
    private readonly DbContextTypesRegistry _dbContextTypes;
    private readonly IOptions<ReadinessMonitorOptions> _options;

    public ReadinessMonitor(
        IServiceScopeFactory scopeFactory,
        IStartupCoordinator coordinator,
        ILogger<ReadinessMonitor> logger,
        ReadinessState state,
        DbContextTypesRegistry dbContextTypes,
        IOptions<ReadinessMonitorOptions> options,
        IRedisConnectionFactory? redisFactory = null,
        IBus? bus = null)
    {
        _scopeFactory = scopeFactory;
        _coordinator = coordinator;
        _logger = logger;
        _state = state;
        _dbContextTypes = dbContextTypes;
        _options = options;
        _redisFactory = redisFactory;
        _bus = bus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Readiness monitor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_coordinator.IsReady)
                {
                    _state.Latest = EvaluateStartupState();
                }
                else
                {
                    _state.Latest = await EvaluateDependenciesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Readiness check failed");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.Value.CheckIntervalSeconds),
                stoppingToken);
        }
    }

    private ReadinessResult EvaluateStartupState()
    {
        var components = new Dictionary<string, ReadinessComponentStatus>();
        var allReady = true;

        var startupReady = CheckStartupTasks();
        components["StartupTasks"] = startupReady;
        allReady &= startupReady.IsHealthy;

        return new ReadinessResult(allReady, components);
    }

    private async Task<ReadinessResult> EvaluateDependenciesAsync(CancellationToken cancellationToken)
    {
        var components = new Dictionary<string, ReadinessComponentStatus>();
        var allReady = true;

        var startupReady = CheckStartupTasks();
        components["StartupTasks"] = startupReady;
        allReady &= startupReady.IsHealthy;

        using var scope = _scopeFactory.CreateScope();

        foreach (var dbContextType in _dbContextTypes.Types)
        {
            var dbResult = await CheckDatabaseAsync(scope, dbContextType, cancellationToken);
            components[$"Database ({dbContextType.Name})"] = dbResult;
            allReady &= dbResult.IsHealthy;
        }

        if (_redisFactory is not null)
        {
            var redisResult = await CheckRedisAsync(cancellationToken);
            components["Redis"] = redisResult;
            allReady &= redisResult.IsHealthy;
        }

        if (_bus is not null)
        {
            var rabbitMqResult = CheckRabbitMq();
            components["RabbitMQ"] = rabbitMqResult;
        }

        return new ReadinessResult(allReady, components);
    }

    private ReadinessComponentStatus CheckStartupTasks()
    {
        if (_coordinator.IsReady)
        {
            var failed = _coordinator.TaskStatuses.Values
                .Any(t => t.Status == StartupTaskStatus.Failed);

            if (!failed)
                return ReadinessComponentStatus.Ready;

            var failedNames = _coordinator.TaskStatuses.Values
                .Where(t => t.Status == StartupTaskStatus.Failed)
                .Select(t => t.DisplayName);
            return new ReadinessComponentStatus(false, $"Startup tasks failed: {string.Join(", ", failedNames)}");
        }

        var pendingOrRunning = _coordinator.TaskStatuses.Values
            .Where(t => t.Status is StartupTaskStatus.Pending or StartupTaskStatus.Running)
            .Select(t => t.DisplayName)
            .ToList();

        var message = pendingOrRunning.Count > 0
            ? $"Startup tasks still running: {string.Join(", ", pendingOrRunning)}"
            : "Startup tasks not yet completed";

        return new ReadinessComponentStatus(false, message);
    }

    private async Task<ReadinessComponentStatus> CheckDatabaseAsync(
        IServiceScope scope, Type dbContextType, CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(dbContextType);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            sw.Stop();

            return canConnect
                ? new ReadinessComponentStatus(true, "Connected", sw.Elapsed)
                : new ReadinessComponentStatus(false, "Cannot connect", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new ReadinessComponentStatus(false, ex.Message);
        }
    }

    private async Task<ReadinessComponentStatus> CheckRedisAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var db = _redisFactory!.GetDatabase();
            await db.PingAsync(StackExchange.Redis.CommandFlags.None);
            sw.Stop();

            return new ReadinessComponentStatus(true, "Connected", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new ReadinessComponentStatus(false, ex.Message);
        }
    }

    private ReadinessComponentStatus CheckRabbitMq()
    {
        try
        {
            return _bus is not null
                ? new ReadinessComponentStatus(true, "Configured")
                : new ReadinessComponentStatus(false, "Not configured");
        }
        catch (Exception ex)
        {
            return new ReadinessComponentStatus(false, ex.Message);
        }
    }
}
