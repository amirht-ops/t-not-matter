using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;

namespace PolicyService.Infrastructure.Persistence.Configurations;

public sealed class UsageLedgerConfiguration : IEntityTypeConfiguration<UsageLedger>
{
    public void Configure(EntityTypeBuilder<UsageLedger> builder)
    {
        builder.ToTable("usage_ledgers");
        builder.HasKey(ledger => new { ledger.TenantId, ledger.Id });
        builder.Property(ledger => ledger.Id).ValueGeneratedNever();
        builder.Property(ledger => ledger.TenantId).ValueGeneratedNever();
        builder.Property(ledger => ledger.ConsumerId)
            .HasColumnName("consumer_id")
            .IsRequired()
            .HasConversion(consumerId => consumerId.Value, value => ConsumerId.From(value));
        builder.Ignore(ledger => ledger.DomainEvents);
        builder.Property(ledger => ledger.Version).IsConcurrencyToken();
        builder.HasIndex(ledger => new { ledger.TenantId, ledger.ConsumerId }).IsUnique();

        // R-1 resolution (Option A): the public List<UsageCounter> maps to a normalized owned table,
        // one row per (action, window).
        builder.OwnsMany(ledger => ledger.Counters, counter =>
        {
            counter.ToTable("usage_counters");
            counter.WithOwner().HasForeignKey("TenantId", "UsageLedgerId");
            counter.Property(c => c.Action)
                .HasColumnName("action")
                .HasMaxLength(256)
                .IsRequired()
                .HasConversion(action => action.Value, value => ActionKey.Create(value).Value!);
            counter.Property(c => c.Count).HasColumnName("count").IsRequired();
            counter.Property(c => c.Window)
                .HasColumnName("window")
                .HasConversion<string>()
                .IsRequired();
            counter.Property(c => c.WindowStart).HasColumnName("window_start").IsRequired();
            counter.Property(c => c.WindowEnd).HasColumnName("window_end").IsRequired();
            // The owned-collection key MUST include the owner FK ("TenantId", "UsageLedgerId");
            // keying on (Action, Window) alone made the counter row globally unique across every
            // tenant/consumer, so only the first consumer per (action, window) could ever persist.
            counter.HasKey("TenantId", "UsageLedgerId", "Action", "Window");
        });

    }
}
