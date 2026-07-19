using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.ValueObjects;

namespace PolicyService.Infrastructure.Persistence.Configurations;

public sealed class QuotaPolicyConfiguration : IEntityTypeConfiguration<QuotaPolicy>
{
    public void Configure(EntityTypeBuilder<QuotaPolicy> builder)
    {
        builder.ToTable("quota_policies");
        builder.HasKey(quotaPolicy => new { quotaPolicy.TenantId, quotaPolicy.Id });
        builder.Property(quotaPolicy => quotaPolicy.Id).ValueGeneratedNever();
        builder.Property(quotaPolicy => quotaPolicy.TenantId).ValueGeneratedNever();
        builder.Property(quotaPolicy => quotaPolicy.Scope)
            .HasColumnName("scope")
            .HasMaxLength(128)
            .IsRequired()
            .HasConversion(scope => ScopeConversion.ToColumn(scope), value => ScopeConversion.FromColumn(value));
        builder.OwnsOne(quotaPolicy => quotaPolicy.Quota, quota =>
        {
            quota.Property(q => q.Daily).HasColumnName("quota_daily").IsRequired()
                .HasConversion(limit => limit.Value, value => QuotaLimit.Create(value).Value!);
            quota.Property(q => q.Weekly).HasColumnName("quota_weekly").IsRequired()
                .HasConversion(limit => limit.Value, value => QuotaLimit.Create(value).Value!);
            quota.Property(q => q.Monthly).HasColumnName("quota_monthly").IsRequired()
                .HasConversion(limit => limit.Value, value => QuotaLimit.Create(value).Value!);
        });
        builder.Property(quotaPolicy => quotaPolicy.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Ignore(quotaPolicy => quotaPolicy.DomainEvents);
        builder.Property(quotaPolicy => quotaPolicy.Version).IsConcurrencyToken();
    }
}
