using System.Collections.Generic;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Events;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.Aggregates.DebtLedgers;

/// <summary>
/// Per-consumer business liability + recovery. Separate aggregate from <see cref="UsageLedger"/>
/// (survives rollover; recovered via future allowance, ADR-018 §5/§6).
///
/// Business rule (authoritative): debt is NOT attached to a quota window. It is a SINGLE
/// outstanding liability per (consumer, action), denominated in allowance units. Windows only
/// determine WHEN renewed allowance appears; any window's renewal repays the single debt first.
/// <see cref="RecoveryPosition"/> is kept per (action, window) — it is the usable allowance for
/// that window after debt deduction, a distinct concept from the debt itself.
///
/// State is held as a <see cref="List{DebtEntry}"/> (one row per action, no window) and a
/// <see cref="List{RecoveryEntry}"/> (one row per action+window), mapping to normalized owned
/// collections under EF Core.
/// </summary>
public sealed class DebtLedger : AggregateRoot
{
    public List<DebtEntry> Debts { get; } = [];
    public List<RecoveryEntry> Recoveries { get; } = [];

    private DebtLedger() { }

    private DebtLedger(Guid id, Guid tenantId, ConsumerId consumerId)
        : base(id, tenantId)
    {
        ConsumerId = consumerId;
    }

    public ConsumerId ConsumerId { get; private set; } = null!;
    public IReadOnlyCollection<DebtAmount> OutstandingDebts => Debts.Select(d => d.Amount).ToList().AsReadOnly();

    public static Result<DebtLedger> Create(Guid tenantId, ConsumerId consumerId)
    {
        if (tenantId == Guid.Empty)
            return Result<DebtLedger>.Failure(PolicyErrors.TenantMismatch);
        if (consumerId is null)
            return Result<DebtLedger>.Failure(PolicyErrors.ConsumerIdRequired);

        return Result<DebtLedger>.Success(new DebtLedger(Guid.NewGuid(), tenantId, consumerId));
    }

    /// <summary>Records excess consumption as the SINGLE (action) debt (invariant 4). No window axis.</summary>
    public Result<Unit> IncurDebt(ActionKey action, long excess, Guid correlationId)
    {
        if (excess <= 0)
            return Result<Unit>.Success(Unit.Value);

        var current = OutstandingDebt(action);
        var updated = current.Add(DebtAmount.Create(excess).Value!);
        UpsertDebt(action, updated);

        MarkUpdated();
        RaiseDomainEvent(new DebtIncurredDomainEvent(DebtLedgerId.From(Id), ConsumerId, action, excess, TenantId, correlationId));
        RaiseDomainEvent(new QuotaExceededDomainEvent(DebtLedgerId.From(Id), ConsumerId, action, excess, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>
    /// Debt-first recovery on a renewal event (invariant 6). The renewal contributes its
    /// <paramref name="renewedAllowance"/> toward the SINGLE outstanding debt; whatever remains
    /// becomes this window's usable allowance (<see cref="RecoveryPosition"/>). <paramref name="boundaryEnd"/>
    /// is the window boundary through which recovery is applied (lazy-recovery marker).
    /// </summary>
    public Result<Unit> Recover(ActionKey action, QuotaWindow window, long renewedAllowance, DateTimeOffset boundaryEnd, Guid correlationId)
    {
        if (renewedAllowance < 0)
            return Result<Unit>.Failure(PolicyErrors.DebtAmountInvalid);

        var debt = OutstandingDebt(action);
        if (debt.Value <= 0)
        {
            UpsertRecovery(action, window, RecoveryPosition.Create(renewedAllowance), boundaryEnd);
            return Result<Unit>.Success(Unit.Value);
        }

        var recovered = Math.Min(renewedAllowance, debt.Value);
        var remaining = debt.Value - recovered;
        UpsertDebt(action, DebtAmount.Create(remaining).Value!);
        UpsertRecovery(action, window, RecoveryPosition.Create(renewedAllowance - recovered), boundaryEnd);

        MarkUpdated();
        RaiseDomainEvent(new DebtRecoveredDomainEvent(DebtLedgerId.From(Id), ConsumerId, action, window, recovered, remaining, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>Lazy-recovery guard: true if no recovery has been applied for the window period starting at <paramref name="boundaryStart"/>.</summary>
    public bool IsRecoveryDue(ActionKey action, QuotaWindow window, DateTimeOffset boundaryStart)
    {
        var entry = Recoveries.FirstOrDefault(r => r.Action == action && r.Window == window);
        return entry is null || entry.WindowEnd < boundaryStart;
    }

    /// <summary>Single outstanding debt for an action (never window-scoped). Invariant 5.</summary>
    public DebtAmount OutstandingDebt(ActionKey action)
    {
        var entry = Debts.FirstOrDefault(d => d.Action == action);
        return entry is not null ? entry.Amount : DebtAmount.Zero;
    }

    /// <summary>Dominance (invariant 7): as long as outstanding debt &gt; 0, no renewed allowance is usable.</summary>
    public DebtAmount OutstandingDebt() => Debts.Aggregate(DebtAmount.Zero, (acc, d) => acc.Add(d.Amount));

    /// <summary>Usable allowance for a window after the last recovery (0 while debt remains).</summary>
    public RecoveryPosition RecoveryPositionFor(ActionKey action, QuotaWindow window)
    {
        var entry = Recoveries.FirstOrDefault(r => r.Action == action && r.Window == window);
        return entry is not null ? entry.Position : RecoveryPosition.Zero;
    }

    /// <summary>Administrative reset clears debt only (never usage, never quota policy).</summary>
    public Result<Unit> Reset(Guid correlationId)
    {
        Debts.Clear();
        Recoveries.Clear();
        MarkUpdated();
        RaiseDomainEvent(new DebtResetDomainEvent(DebtLedgerId.From(Id), ConsumerId, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public DebtLedgerId GetDebtLedgerId() => DebtLedgerId.From(Id);

    private void UpsertDebt(ActionKey action, DebtAmount amount)
    {
        var entry = DebtEntry.Create(action, amount.Value);
        if (entry.IsFailure)
            return;

        var index = Debts.FindIndex(d => d.Action == action);
        if (index >= 0)
            Debts[index] = entry.Value!;
        else
            Debts.Add(entry.Value!);
    }

    private void UpsertRecovery(ActionKey action, QuotaWindow window, RecoveryPosition position, DateTimeOffset boundaryEnd)
    {
        var entry = RecoveryEntry.Create(action, window, position.Remaining, boundaryEnd);
        if (entry.IsFailure)
            return;

        var index = Recoveries.FindIndex(r => r.Action == action && r.Window == window);
        if (index >= 0)
            Recoveries[index] = entry.Value!;
        else
            Recoveries.Add(entry.Value!);
    }
}
