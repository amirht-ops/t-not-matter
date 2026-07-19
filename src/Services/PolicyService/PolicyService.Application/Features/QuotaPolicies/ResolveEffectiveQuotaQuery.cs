using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.Services;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Application;
using SharedKernel.Results;

namespace PolicyService.Application.Features.QuotaPolicies;

/// <summary>
/// UC-19 — Resolve the effective quota for a scope chain (Pipeline stage 3). Cached (ADR-016 /
/// cache report §2). The cache key is tenant-scoped via the Tenant scope in the candidate scopes.
/// </summary>
public sealed record ResolveEffectiveQuotaQuery(IReadOnlyList<SubscriptionScope> CandidateScopes)
    : IRequest<Result<QuotaPolicyDto>>, ICachedQuery
{
    public string CacheKey =>
        "policy:effective-quota:" + string.Join("|", CandidateScopes.Select(s => s.ToString()));

    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(5);
}

public sealed class ResolveEffectiveQuotaQueryValidator : AbstractValidator<ResolveEffectiveQuotaQuery>
{
    public ResolveEffectiveQuotaQueryValidator()
    {
        RuleFor(x => x.CandidateScopes).NotEmpty();
    }
}

public sealed class ResolveEffectiveQuotaQueryHandler(
    IQuotaResolver quotaResolver,
    IRequestContextAccessor requestContext)
    : IRequestHandler<ResolveEffectiveQuotaQuery, Result<QuotaPolicyDto>>
{
    public async Task<Result<QuotaPolicyDto>> Handle(ResolveEffectiveQuotaQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var quotaPolicy = await quotaResolver.ResolveAsync(context.TenantId, request.CandidateScopes, cancellationToken);
        if (quotaPolicy.IsFailure)
            return Result<QuotaPolicyDto>.Failure(quotaPolicy.Error);

        return Result<QuotaPolicyDto>.Success(GetQuotaPolicyByIdQueryHandler.Map(quotaPolicy.Value!));
    }
}
