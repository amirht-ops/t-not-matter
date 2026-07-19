using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>Resolves the effective Subscription for a scope chain (Pipeline stage 1).</summary>
public sealed class SubscriptionResolver : ISubscriptionResolver
{
    private readonly ISubscriptionRepository _repository;

    public SubscriptionResolver(ISubscriptionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Subscription>> ResolveAsync(Guid tenantId, IReadOnlyCollection<SubscriptionScope> candidateScopes, CancellationToken cancellationToken = default)
    {
        var candidates = new List<Subscription>();
        foreach (var scope in candidateScopes)
        {
            var subscription = await _repository.GetByScopeAsync(tenantId, scope, cancellationToken);
            if (subscription is not null && subscription.Status == SubscriptionStatus.Active)
                candidates.Add(subscription);
        }

        if (candidates.Count == 0)
            return Result<Subscription>.Failure(PolicyErrors.SubscriptionNotFound);

        var effective = candidates
            .OrderByDescending(s => (int)s.Scope.Kind)
            .ThenBy(s => s.EffectiveFrom)
            .First();

        return Result<Subscription>.Success(effective);
    }
}
