using IdentityService.Domain.Services;
using IdentityService.Infrastructure.Persistence;
using IdentityService.Infrastructure.Persistence.Seed;

namespace IdentityService.Api.Extensions;

public static class SeedExtensions
{
    public static async Task SeedIdentityDataAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<IdentityDbContext>>();

        try
        {
            await IdentityDataSeeder.SeedAsync(dbContext, passwordHasher, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding identity data");
            throw;
        }
    }
}
