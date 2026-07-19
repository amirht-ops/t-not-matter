using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Specifications;

/// <summary>Effective QuotaPolicy = highest precedence scope (User &gt; Role &gt; Tenant), first-defined wins.</summary>
public sealed class QuotaResolutionSpecification : IQuotaResolutionSpecification
{
    public Result<QuotaPolicy> Resolve(IReadOnlyCollection<QuotaPolicy> candidates)
    {
        if (candidates.Count == 0)
            return Result<QuotaPolicy>.Failure(PolicyErrors.QuotaPolicyNotFound);

        var effective = candidates
            .OrderByDescending(p => (int)p.Scope.Kind)
            .ThenBy(p => p.CreatedAt)
            .First();

        return Result<QuotaPolicy>.Success(effective);
    }
}
