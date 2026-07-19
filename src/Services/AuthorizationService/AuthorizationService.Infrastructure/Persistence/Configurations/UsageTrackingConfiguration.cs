using AuthorizationService.Domain.Aggregates.UsageTracking;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Infrastructure.Persistence.Configurations;

public sealed class UsageTrackingConfiguration : IEntityTypeConfiguration<UsageTracking>
{
    public void Configure(EntityTypeBuilder<UsageTracking> builder)
    {
        builder.ToTable("UsageTrackings");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TenantId)
            .IsRequired();
        builder.Property(t => t.SubjectId)
            .HasConversion(t => t.Value, v => SubjectId.From(v))
            .IsRequired();
        builder.Property(t => t.Action).IsRequired().HasMaxLength(100);
        builder.Property(t => t.ResourceType).IsRequired().HasMaxLength(100);
        builder.Property(t => t.ResourceId).IsRequired().HasMaxLength(200);
        builder.Property(t => t.WindowStart).IsRequired();
        builder.Property(t => t.WindowEnd).IsRequired();
        builder.Property(t => t.Count).IsRequired();
        builder.Property(t => t.LastAccessedAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();
        builder.Property(t => t.Version).IsRequired().IsConcurrencyToken();
        builder.Property(t => t.IsDeleted).IsRequired();
        
        builder.OwnsOne(t => t.Window, w =>
        {
            w.Property(w => w.Type).HasColumnName("WindowType").IsRequired();
            w.Property(w => w.DurationSeconds).HasColumnName("WindowDurationSeconds").IsRequired();
            w.Property(w => w.Start).HasColumnName("WindowStart").IsRequired();
            w.Property(w => w.End).HasColumnName("WindowEnd").IsRequired();
        });

        builder.HasIndex(t => new { t.TenantId, t.SubjectId, t.Action, t.ResourceType, t.ResourceId, t.WindowStart, t.WindowEnd })
            .IsUnique()
            .HasDatabaseName("IX_UsageTracking_Tenant_Subject_Action_Resource_Window");
        
        builder.HasIndex(t => t.TenantId).HasDatabaseName("IX_UsageTracking_TenantId");
        builder.HasIndex(t => t.WindowEnd).HasDatabaseName("IX_UsageTracking_WindowEnd");
    }
}