using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Infrastructure.Persistence.Configurations;

public sealed class DeadLetterMessageConfiguration : IEntityTypeConfiguration<Platform.Infrastructure.Outbox.DeadLetterMessage>
{
    public void Configure(EntityTypeBuilder<Platform.Infrastructure.Outbox.DeadLetterMessage> builder)
    {
        builder.ToTable("identity_dead_letter");
        builder.HasKey(dl => dl.Id);
        builder.HasIndex(dl => new { dl.TenantId, dl.MovedAt });
        builder.Property(dl => dl.EventType).HasMaxLength(256);
        builder.Property(dl => dl.Payload).HasColumnType("jsonb");
        builder.Property(dl => dl.Error).HasMaxLength(2048);
    }
}