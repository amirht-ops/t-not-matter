using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.UsageLedgers;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>Administrative Reset: clears usage + debt, never touches QuotaPolicy (invariant 10).</summary>
public sealed class AllowanceAdministrationService : IAllowanceAdministrationService
{
    public Result<Unit> Reset(UsageLedger usageLedger, DebtLedger debtLedger, Guid correlationId)
    {
        var usage = usageLedger.Reset(correlationId);
        if (usage.IsFailure)
            return usage;
        return debtLedger.Reset(correlationId);
    }
}
