using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class DatabaseStartupTask<TDbContext> : IStartupTask
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseStartupTask<TDbContext>> _logger;

    public DatabaseStartupTask(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseStartupTask<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string DisplayName => $"Database ({typeof(TDbContext).Name})";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        _logger.LogInformation("Warming up database connection and EF Core model for {DbContext}", typeof(TDbContext).Name);

        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            _logger.LogWarning("Database {DbContext} is not reachable yet", typeof(TDbContext).Name);
            return;
        }

        _logger.LogInformation("Database {DbContext} is reachable", typeof(TDbContext).Name);
    }
}
