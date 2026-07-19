using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Abstractions.Tenant;
using PlatformRequestContext = Platform.Abstractions.Tenant.RequestContext;

namespace AuthorizationService.Infrastructure.Persistence;

public sealed class AuthorizationDbContextFactory : IDesignTimeDbContextFactory<AuthorizationDbContext>
{
    public AuthorizationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuthorizationDbContext>();
        var connectionString = args.Length > 0
            ? args[0]
            : "Host=localhost;Port=5432;Database=authorization_db;Username=postgres;Password=Developer1245;Include Error Detail=true";
        optionsBuilder.UseNpgsql(connectionString);

        return new AuthorizationDbContext(optionsBuilder.Options, new NullRequestContext());
    }

    private sealed class NullRequestContext : IRequestContextAccessor
    {
        public PlatformRequestContext Context { get; set; } = new(
            Guid.Empty, Guid.Empty, Guid.Empty);
    }
}
