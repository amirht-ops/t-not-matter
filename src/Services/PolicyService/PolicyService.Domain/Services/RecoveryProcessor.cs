using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>
/// Applies debt-first recovery on a renewal event (Pipeline: Recovery stage). Operates on the
/// SINGLE OutstandingDebt per (consumer, action); the <paramref name="window"/> only selects which
/// renewed allowance is applied and records that window's post-repayment usable allowance.
/// Debt is window-agnostic; only the renewed allowance and the persisted <see cref="RecoveryPosition"/>
/// are window-specific. Invoked lazily by <see cref="AllowanceEngine"/> before each consumption
/// (no scheduler). The recovery logic (including the decision to reduce debt only when outstanding
/// debt &gt; 0, and to always persist the per-window <see cref="RecoveryPosition"/>) lives in
/// <see cref="DebtLedger.Recover"/>, keeping the aggregate as the owner of its invariants.
/// </summary>
public sealed class RecoveryProcessor : IRecoveryProcessor
{
    public Result<Unit> Recover(
        DebtLedger debtLedger,
        ActionKey actionKey,
        QuotaWindow window,
        long renewedAllowance,
        DateTimeOffset boundaryEnd,
        Guid correlationId)
        => debtLedger.Recover(actionKey, window, renewedAllowance, boundaryEnd, correlationId);
}
