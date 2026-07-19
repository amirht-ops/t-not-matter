using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Specifications;

/// <summary>Walks User→Role→Tenant and returns the effective QuotaPolicy (ADR-018 §8, §14).</summary>
public interface IQuotaResolutionSpecification
{
    Result<QuotaPolicy> Resolve(IReadOnlyCollection<QuotaPolicy> candidates);
}

/// <summary>Any outstanding debt (single per action) dominates all windows (invariant 7).</summary>
public interface IDebtDominanceSpecification
{
    bool IsDominant(DebtAmount debt);
}

/// <summary>Remaining allowance (after debt) covers the requested consumption (invariant 6).</summary>
public interface IAllowanceSufficiencySpecification
{
    bool IsSufficient(RecoveryPosition remaining, ConsumedUnits requested);
}
