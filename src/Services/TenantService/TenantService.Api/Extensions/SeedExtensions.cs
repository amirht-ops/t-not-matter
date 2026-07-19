using TenantService.Infrastructure.Persistence;
using TenantService.Infrastructure.Persistence.Seed;

namespace TenantService.Api.Extensions;

public static class SeedExtensions
{
    public static async Task SeedTenantDataAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TenantDbContext>>();

        try
        {
            await TenantDataSeeder.SeedAsync(dbContext, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding tenant data");
            throw;
        }
    }
}
