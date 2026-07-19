using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PlatformRequestContext = Platform.Abstractions.Tenant.RequestContext;

namespace IdentityService.Infrastructure.Persistence;

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        var connectionString = args.Length > 0
            ? args[0]
            : "Host=localhost;Port=5432;Database=identity_db;Username=postgres;Password=Developer1245;Include Error Detail=true";
        optionsBuilder.UseNpgsql(connectionString);

        return new IdentityDbContext(optionsBuilder.Options, new NullRequestContextAccessor());
    }

    private sealed class NullRequestContextAccessor : Platform.Abstractions.Tenant.IRequestContextAccessor
    {
        public PlatformRequestContext Context { get; set; } = new(
            Guid.Empty, Guid.Empty, Guid.Empty);
    }
}
