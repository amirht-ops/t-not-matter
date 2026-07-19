using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.Specifications;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>Resolves the effective QuotaPolicy for a scope chain (Pipeline stage 3).</summary>
public sealed class QuotaResolver : IQuotaResolver
{
    private readonly IQuotaPolicyRepository _repository;
    private readonly IQuotaResolutionSpecification _specification;

    public QuotaResolver(IQuotaPolicyRepository repository, IQuotaResolutionSpecification specification)
    {
        _repository = repository;
        _specification = specification;
    }

    public async Task<Result<QuotaPolicy>> ResolveAsync(Guid tenantId, IReadOnlyCollection<SubscriptionScope> candidateScopes, CancellationToken cancellationToken = default)
    {
        var candidates = new List<QuotaPolicy>();
        foreach (var scope in candidateScopes)
        {
            var policy = await _repository.GetByScopeAsync(tenantId, scope, cancellationToken);
            if (policy is not null)
                candidates.Add(policy);
        }

        if (candidates.Count == 0)
            return Result<QuotaPolicy>.Failure(PolicyErrors.QuotaPolicyNotFound);

        return _specification.Resolve(candidates);
    }
}
