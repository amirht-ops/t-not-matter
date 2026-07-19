using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Abstractions.Tenant;
using Platform.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` can build <see cref="PolicyDbContext"/>
/// without standing up the full host (Redis / MassTransit / consumers). Mirrors the other
/// services' factories. Applies the shared <see cref="TenantRlsInterceptor"/> so the RLS
/// migration generator is exercised.
/// </summary>
public sealed class PolicyDbContextFactory : IDesignTimeDbContextFactory<PolicyDbContext>
{
    public PolicyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PolicyDbContext>();
        var connectionString = args.Length > 0
            ? args[0]
            : "Host=localhost;Port=5432;Database=policyservice;Username=postgres;Password=postgres;Include Error Detail=true";
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.AddInterceptors(new TenantRlsInterceptor(new NullServiceProvider()));

        return new PolicyDbContext(optionsBuilder.Options, new NullRequestContextAccessor());
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        // The RLS interceptor resolves IRequestContextAccessor from the provider; supply a
        // design-time stub so `dotnet ef` can build the context without the full host.
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IRequestContextAccessor) ? new NullRequestContextAccessor() : null;
    }


    private sealed class NullRequestContextAccessor : IRequestContextAccessor
    {
        public RequestContext Context { get; set; } = new(
            TenantId: Guid.Empty,
            CorrelationId: Guid.Empty,
            RequestId: Guid.Empty);
    }
}
