using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Outbox;

namespace PolicyService.Infrastructure.Persistence.Configurations;

public sealed class DeadLetterMessageConfiguration : IEntityTypeConfiguration<DeadLetterMessage>
{
    public void Configure(EntityTypeBuilder<DeadLetterMessage> builder)
    {
        builder.ToTable("policy_dead_letter");
        builder.HasKey(dl => dl.Id);
        builder.HasIndex(dl => new { dl.TenantId, dl.MovedAt });
        builder.Property(dl => dl.EventType).HasMaxLength(256);
        builder.Property(dl => dl.Payload).HasColumnType("jsonb");
        builder.Property(dl => dl.Error).HasMaxLength(2048);
    }
}
