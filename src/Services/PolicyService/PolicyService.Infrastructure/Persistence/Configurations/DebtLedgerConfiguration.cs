using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;

namespace PolicyService.Infrastructure.Persistence.Configurations;

// Explicit converters so EF treats DebtAmount / RecoveryPosition as converted scalar columns
// (not nested owned entities) when used as properties of the owned-many DebtEntry / RecoveryEntry.
file sealed class DebtConverters
{
    public static readonly ValueConverter<DebtAmount, long> DebtAmountConverter =
        new(v => v.Value, v => DebtAmount.Create(v).Value!);
    public static readonly ValueConverter<RecoveryPosition, long> RecoveryPositionConverter =
        new(v => v.Remaining, v => RecoveryPosition.Create(v));
}

public sealed class DebtLedgerConfiguration : IEntityTypeConfiguration<DebtLedger>
{
    public void Configure(EntityTypeBuilder<DebtLedger> builder)
    {
        builder.ToTable("debt_ledgers");
        builder.HasKey(ledger => new { ledger.TenantId, ledger.Id });
        builder.Property(ledger => ledger.Id).ValueGeneratedNever();
        builder.Property(ledger => ledger.TenantId).ValueGeneratedNever();
        builder.Property(ledger => ledger.ConsumerId)
            .HasColumnName("consumer_id")
            .IsRequired()
            .HasConversion(consumerId => consumerId.Value, value => ConsumerId.From(value));
        builder.Ignore(ledger => ledger.OutstandingDebts);
        builder.Ignore(ledger => ledger.DomainEvents);
        builder.Property(ledger => ledger.Version).IsConcurrencyToken();
        builder.HasIndex(ledger => new { ledger.TenantId, ledger.ConsumerId }).IsUnique();

        // R-1 resolution (Option A): debt is a SINGLE outstanding liability per (action) — NO window
        // axis (business rule). Recovery markers are per (action, window) and carry the window's
        // post-repayment usable allowance plus the boundary through which recovery was applied.
        builder.OwnsMany(ledger => ledger.Debts, debt =>
        {
            debt.ToTable("debt_entries");
            debt.WithOwner().HasForeignKey("TenantId", "DebtLedgerId");
            debt.Property(d => d.Action)
                .HasColumnName("action")
                .HasMaxLength(256)
                .IsRequired()
                .HasConversion(action => action.Value, value => ActionKey.Create(value).Value!);
            debt.Property(d => d.Amount)
                .HasColumnName("amount")
                .IsRequired()
                .HasConversion(DebtConverters.DebtAmountConverter);
            // Key MUST include the owner FK ("TenantId", "DebtLedgerId"); keying on (Action) alone
            // made a debt entry globally unique per action across all tenants/consumers.
            debt.HasKey("TenantId", "DebtLedgerId", "Action");
        });


        builder.OwnsMany(ledger => ledger.Recoveries, recovery =>
        {
            recovery.ToTable("recovery_entries");
            recovery.WithOwner().HasForeignKey("TenantId", "DebtLedgerId");
            recovery.Property(r => r.Action)
                .HasColumnName("action")
                .HasMaxLength(256)
                .IsRequired()
                .HasConversion(action => action.Value, value => ActionKey.Create(value).Value!);
            recovery.Property(r => r.Window)
                .HasColumnName("window")
                .HasConversion<string>()
                .IsRequired();
            recovery.Property(r => r.Position)
                .HasColumnName("remaining")
                .IsRequired()
                .HasConversion(DebtConverters.RecoveryPositionConverter);
            recovery.Property(r => r.WindowEnd)
                .HasColumnName("window_end")
                .IsRequired();
            // Key MUST include the owner FK ("TenantId", "DebtLedgerId"); keying on (Action, Window)
            // alone made a recovery marker globally unique across all tenants/consumers.
            recovery.HasKey("TenantId", "DebtLedgerId", "Action", "Window");
        });

    }
}
