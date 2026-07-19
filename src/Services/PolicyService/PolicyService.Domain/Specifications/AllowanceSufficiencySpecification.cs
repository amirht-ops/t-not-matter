using PolicyService.Domain.ValueObjects;

namespace PolicyService.Domain.Specifications;

/// <summary>Remaining allowance (after debt) covers the requested consumption (invariant 6).</summary>
public sealed class AllowanceSufficiencySpecification : IAllowanceSufficiencySpecification
{
    public bool IsSufficient(RecoveryPosition remaining, ConsumedUnits requested) => remaining.Remaining >= requested.Value;
}
