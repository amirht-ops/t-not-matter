using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Abstractions.Tenant;

namespace TenantService.Infrastructure.Persistence;

public sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
        var connectionString = args.Length > 0
            ? args[0]
            : "Host=localhost;Port=5432;Database=tenant_db;Username=postgres;Password=Developer1245;Include Error Detail=true";
        optionsBuilder.UseNpgsql(connectionString);

        return new TenantDbContext(optionsBuilder.Options, new NullRequestContextAccessor());
    }

    private sealed class NullRequestContextAccessor : IRequestContextAccessor
    {
        public RequestContext Context { get; set; } = new(
            TenantId: Guid.Empty,
            CorrelationId: Guid.Empty,
            RequestId: Guid.Empty);
    }
}
