using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.ValueObjects;

namespace PolicyService.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(subscription => new { subscription.TenantId, subscription.Id });
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();
        builder.Property(subscription => subscription.TenantId).ValueGeneratedNever();
        builder.Property(subscription => subscription.PolicyId)
            .HasColumnName("policy_id")
            .HasConversion(policyId => policyId.Value, value => PolicyId.From(value));
        builder.Property(subscription => subscription.Scope)
            .HasColumnName("scope")
            .HasMaxLength(128)
            .IsRequired()
            .HasConversion(scope => ScopeConversion.ToColumn(scope), value => ScopeConversion.FromColumn(value));
        builder.Property(subscription => subscription.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(subscription => subscription.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Ignore(subscription => subscription.DomainEvents);
        builder.Property(subscription => subscription.Version).IsConcurrencyToken();
    }
}
