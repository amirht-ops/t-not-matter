using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolicyService.Domain.Aggregates.PrincipalHierarchy;

namespace PolicyService.Infrastructure.Persistence.Configurations;

public sealed class PrincipalEdgeConfiguration : IEntityTypeConfiguration<PrincipalEdge>
{
    public void Configure(EntityTypeBuilder<PrincipalEdge> builder)
    {
        builder.ToTable("principal_edges");
        builder.HasKey(edge => new { edge.TenantId, edge.UserId, edge.RoleId });
        builder.Property(edge => edge.Id).ValueGeneratedNever();
        builder.Property(edge => edge.TenantId).ValueGeneratedNever();
        builder.Property(edge => edge.UserId).ValueGeneratedNever();
        builder.Property(edge => edge.RoleId).ValueGeneratedNever();
        builder.Property(edge => edge.Version).IsConcurrencyToken();
        builder.HasIndex(edge => new { edge.TenantId, edge.UserId, edge.RoleId }).IsUnique();
    }
}
