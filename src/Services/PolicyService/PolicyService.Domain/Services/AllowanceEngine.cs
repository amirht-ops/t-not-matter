using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Specifications;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>
/// Coordinates a consumption (Pipeline stages 4–7): debt resolution → lazy recovery → decision →
/// usage recording → single debt update. Pure: receives the already-loaded ledgers + resolved
/// quota, mutates them, and raises events. The caller persists atomically (ADR-015).
///
/// Debt model (authoritative): debt is a SINGLE outstanding liability per (consumer, action),
/// NOT per window. Recovery is triggered lazily before each consumption: any window whose period
/// has rolled over since the last recovery contributes its renewed allowance toward the single
/// debt first (invariant 6). While OutstandingDebt(action) &gt; 0, no renewed allowance is usable
/// (invariant 7); the operation still "succeeds" (never truncated) but is denied at the allowance
/// level. Dominance is decided by <see cref="IDebtDominanceSpecification"/>.
/// </summary>
public sealed class AllowanceEngine : IAllowanceEngine
{
    private readonly IRecoveryProcessor _recovery;
    private readonly IDebtDominanceSpecification _dominance;

    public AllowanceEngine(IRecoveryProcessor recovery, IDebtDominanceSpecification dominance)
    {
        _recovery = recovery;
        _dominance = dominance;
    }

    public Result<ConsumptionDecision> Consume(
        UsageLedger usageLedger,
        DebtLedger debtLedger,
        Quota effectiveQuota,
        ActionKey actionKey,
        ConsumedUnits units,
        Guid correlationId)
    {
        var now = DateTimeOffset.UtcNow;

        // Lazy recovery (first access after renewal): for each window, if its period has rolled
        // over since the last recovery, apply the renewal (renewed allowance = the window's quota
        // limit) toward the single debt first. Every elapsed Daily/Weekly/Monthly renewal is
        // processed; recovery is debt-first and persists the per-window RecoveryPosition.
        foreach (var window in new[] { QuotaWindow.Daily, QuotaWindow.Weekly, QuotaWindow.Monthly })
        {
            var (start, end) = UsageLedger.GetWindowBoundariesPublic(window, now);
            if (debtLedger.IsRecoveryDue(actionKey, window, start))
            {
                var renewed = effectiveQuota.LimitFor(window);
                var recovery = _recovery.Recover(debtLedger, actionKey, window, renewed, end, correlationId);
                if (recovery.IsFailure)
                    return Result<ConsumptionDecision>.Failure(recovery.Error);
            }
        }

        // Dominance (invariant 7): any outstanding debt blocks all renewed allowance for this
        // action. Decided by the DebtDominanceSpecification (single per-action debt).
        if (_dominance.IsDominant(debtLedger.OutstandingDebt(actionKey)))
        {
            usageLedger.MarkDenied(actionKey, PolicyErrors.ConsumptionBlockedByDebt.Description, correlationId);
            return Result<ConsumptionDecision>.Success(ConsumptionDecision.Deny(PolicyErrors.ConsumptionBlockedByDebt.Description));
        }

        // Consumption Decision → Usage Recording → single Debt Update (stages 5–7).
        // Usage is recorded per window (Daily/Weekly/Monthly are active simultaneously and reset
        // on their own boundaries), but debt is a SINGLE liability per (consumer, action) — never
        // per window. The windows nest (Daily ⊂ Weekly ⊂ Monthly), so the Monthly tally is the
        // binding aggregate capacity: an operation that pushes the monthly count over its limit
        // incurs exactly one debt equal to that overage. Summing per-window excess would
        // triple-count a single operation, so the debt is derived from the monthly overage only.
        // Debt is never reduced here (recovery does that); we only ever increase it to the
        // highest monthly overage observed, so it is never double-counted.
        foreach (var window in new[] { QuotaWindow.Daily, QuotaWindow.Weekly, QuotaWindow.Monthly })
        {
            var record = usageLedger.Record(usageLedger.ConsumerId, actionKey, window, units.Value, now, correlationId);
            if (record.IsFailure)
                return Result<ConsumptionDecision>.Failure(record.Error);
        }

        var monthlyCount = usageLedger.GetCount(actionKey, QuotaWindow.Monthly, now);
        var monthlyLimit = effectiveQuota.LimitFor(QuotaWindow.Monthly);
        var monthlyExcess = monthlyCount > monthlyLimit ? monthlyCount - monthlyLimit : 0;
        var currentDebt = debtLedger.OutstandingDebt(actionKey).Value;
        if (monthlyExcess > currentDebt)
        {
            var incur = debtLedger.IncurDebt(actionKey, monthlyExcess - currentDebt, correlationId);
            if (incur.IsFailure)
                return Result<ConsumptionDecision>.Failure(incur.Error);
        }

        usageLedger.MarkAllowed(actionKey, correlationId);
        return Result<ConsumptionDecision>.Success(ConsumptionDecision.Allow());
    }
}
