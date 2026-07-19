using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.ValueObjects;

namespace PolicyService.Infrastructure.Persistence.Configurations;

public sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("policies");
        builder.HasKey(policy => new { policy.TenantId, policy.Id });
        builder.Property(policy => policy.Id).ValueGeneratedNever();
        builder.Property(policy => policy.TenantId).ValueGeneratedNever();
        builder.Property(policy => policy.Name).IsRequired().HasMaxLength(256);
        builder.Property(policy => policy.Condition)
            .HasColumnName("condition")
            .HasColumnType("text")
            .HasConversion(condition => condition.Value, value => PolicyCondition.Create(value).Value!);
        builder.Property(policy => policy.Expression)
            .HasColumnName("expression")
            .HasColumnType("text")
            .HasConversion(expression => expression.Value, value => PolicyExpression.Create(value).Value!);
        builder.Property(policy => policy.Priority)
            .HasColumnName("priority_rank")
            .HasConversion(priority => priority.Rank, value => PolicyPriority.Create(value).Value!);
        builder.Property(policy => policy.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(policy => policy.CompiledRego)
            .HasColumnName("compiled_rego")
            .HasColumnType("text")
            .HasConversion(
                rego => rego == null ? null : rego.Source,
                value => value == null ? null : RegoModule.Create(value).Value!);
        builder.Ignore(policy => policy.DomainEvents);
        builder.Property(policy => policy.Version).IsConcurrencyToken();
    }
}
