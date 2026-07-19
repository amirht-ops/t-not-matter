using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolicyService.Domain.Aggregates.PrincipalHierarchy;
using PolicyService.Domain.Enums;

namespace PolicyService.Infrastructure.Persistence.Configurations;

public sealed class PrincipalNodeConfiguration : IEntityTypeConfiguration<PrincipalNode>
{
    public void Configure(EntityTypeBuilder<PrincipalNode> builder)
    {
        builder.ToTable("principal_nodes");
        builder.HasKey(node => new { node.TenantId, node.NodeId });
        builder.Property(node => node.Id).ValueGeneratedNever();
        builder.Property(node => node.TenantId).ValueGeneratedNever();
        builder.Property(node => node.NodeId).ValueGeneratedNever();
        builder.Property(node => node.NodeType)
            .HasColumnName("node_type")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(node => node.Version).IsConcurrencyToken();
        builder.HasIndex(node => new { node.TenantId, node.NodeId }).IsUnique();
    }
}
