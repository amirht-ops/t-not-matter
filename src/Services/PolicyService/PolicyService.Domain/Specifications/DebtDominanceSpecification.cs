using PolicyService.Domain.ValueObjects;

namespace PolicyService.Domain.Specifications;

/// <summary>Any outstanding debt (single per action) dominates all windows (invariant 7).</summary>
public sealed class DebtDominanceSpecification : IDebtDominanceSpecification
{
    public bool IsDominant(DebtAmount debt) => debt.Value > 0;
}
