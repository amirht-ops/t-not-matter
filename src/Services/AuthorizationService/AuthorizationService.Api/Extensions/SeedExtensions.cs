using AuthorizationService.Infrastructure.Persistence;
using AuthorizationService.Infrastructure.Persistence.Seed;

namespace AuthorizationService.Api.Extensions;

public static class SeedExtensions
{
    public static async Task SeedAuthorizationDataAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuthorizationDbContext>>();

        try
        {
            await AuthorizationDataSeeder.SeedAsync(dbContext, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding authorization data");
            throw;
        }
    }
}
