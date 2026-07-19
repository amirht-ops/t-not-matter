using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Infrastructure.Extensions;

public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync<TDbContext>(this IHost host)
        where TDbContext : DbContext
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TDbContext>>();

        try
        {
            var pending = await dbContext.Database.GetPendingMigrationsAsync();
            var pendingList = pending.ToList();

            if (pendingList.Count == 0)
            {
                logger.LogDebug("No pending migrations for {DbContext}", typeof(TDbContext).Name);
                return;
            }

            logger.LogInformation(
                "Applying {Count} pending migration(s) for {DbContext}",
                pendingList.Count,
                typeof(TDbContext).Name);

            await dbContext.Database.MigrateAsync();

            logger.LogInformation(
                "Migrations applied successfully for {DbContext}",
                typeof(TDbContext).Name);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "An error occurred while applying migrations for {DbContext}",
                typeof(TDbContext).Name);
            throw;
        }
    }
}
