using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.Services;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Application;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Subscriptions;

/// <summary>
/// UC-13 — Resolve the effective subscription for a scope chain (Pipeline stage 1). Cached
/// (ADR-016 / cache report §2): config-driven, low churn, read on every consumption. The cache
/// key is tenant-scoped via the Tenant scope included in the candidate scopes.
/// </summary>
public sealed record ResolveEffectiveSubscriptionQuery(IReadOnlyList<SubscriptionScope> CandidateScopes)
    : IRequest<Result<SubscriptionDto>>, ICachedQuery
{
    public string CacheKey =>
        "policy:effective-subscription:" + string.Join("|", CandidateScopes.Select(s => s.ToString()));

    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(5);
}

public sealed class ResolveEffectiveSubscriptionQueryValidator : AbstractValidator<ResolveEffectiveSubscriptionQuery>
{
    public ResolveEffectiveSubscriptionQueryValidator()
    {
        RuleFor(x => x.CandidateScopes).NotEmpty();
    }
}

public sealed class ResolveEffectiveSubscriptionQueryHandler(
    ISubscriptionResolver subscriptionResolver,
    IRequestContextAccessor requestContext)
    : IRequestHandler<ResolveEffectiveSubscriptionQuery, Result<SubscriptionDto>>
{
    public async Task<Result<SubscriptionDto>> Handle(ResolveEffectiveSubscriptionQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var subscription = await subscriptionResolver.ResolveAsync(context.TenantId, request.CandidateScopes, cancellationToken);
        if (subscription.IsFailure)
            return Result<SubscriptionDto>.Failure(subscription.Error);

        return Result<SubscriptionDto>.Success(GetSubscriptionByIdQueryHandler.Map(subscription.Value!));
    }
}
