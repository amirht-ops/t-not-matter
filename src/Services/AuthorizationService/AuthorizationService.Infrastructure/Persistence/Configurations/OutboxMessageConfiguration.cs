using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Infrastructure.Outbox;

namespace AuthorizationService.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("authorization_outbox");
        builder.HasKey(message => message.Id);
        builder.HasIndex(message => new { message.TenantId, message.ProcessedAt, message.NextRetryAt });
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.EventType).HasMaxLength(256);
        builder.Property(message => message.Payload).HasColumnType("jsonb");
        builder.Property(message => message.Error).HasMaxLength(2048);
    }
}
