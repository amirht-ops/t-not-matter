using IdentityService.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures AccessRisk as a 1:1 extension of User authentication state.
/// 
/// Concurrency model: Optimistic concurrency (Version) is EXPLICITLY disabled.
/// AccessRisk is high-frequency telemetry — locking on every login attempt would
/// create a contention hotspot. See ADR-008 Amendment 1.
/// 
/// Persistence: EF Core uses the private constructor via reflection. The Hydrate()
/// factory has been removed as EF Core materializes entities without it.
/// </summary>
public sealed class AccessRiskConfiguration : IEntityTypeConfiguration<AccessRisk>
{
    public void Configure(EntityTypeBuilder<AccessRisk> builder)
    {
        builder.ToTable("identity_access_risks");

        // PK is UserId — 1:1 relationship with User for security telemetry
        builder.HasKey(risk => new { risk.TenantId, risk.Id });

        builder.Property(risk => risk.Id).ValueGeneratedNever();
        builder.Property(risk => risk.TenantId).ValueGeneratedNever();
        builder.Property(risk => risk.FailedCount).IsRequired();
        builder.Property(risk => risk.LastFailureAt);

        // Explicitly disable optimistic concurrency for high-throughput login attempts.
        // AccessRisk does not participate in OCC — it's fire-and-forget telemetry.
        builder.Property(risk => risk.Version).IsConcurrencyToken(false);

        builder.Ignore(risk => risk.UserId);
        builder.Ignore(risk => risk.DomainEvents);
    }
}
