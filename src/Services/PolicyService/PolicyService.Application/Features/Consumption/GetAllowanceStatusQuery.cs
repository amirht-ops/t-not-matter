using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.Services;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Application;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Consumption;

public sealed record WindowAllowanceDto(string Window, long Limit, long Used, long OutstandingDebt, long Remaining);

public sealed record AllowanceStatusDto(IReadOnlyCollection<WindowAllowanceDto> Windows);

/// <summary>
/// UC-23 — Composite allowance status (effective quota − usage − debt) per window. Short-TTL
/// cache (≤30s, cache report §2) bounds staleness on the high-churn counters; never masks live
/// debt. The cache key is tenant-safe because the consumer GUID is globally unique.
/// </summary>
public sealed record GetAllowanceStatusQuery(Guid ConsumerId, string ActionKey)
    : IRequest<Result<AllowanceStatusDto>>, ICachedQuery
{
    public string CacheKey => $"policy:allowance-status:{ConsumerId:N}:{ActionKey}";

    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromSeconds(30);
}

public sealed class GetAllowanceStatusQueryValidator : AbstractValidator<GetAllowanceStatusQuery>
{
    public GetAllowanceStatusQueryValidator()
    {
        RuleFor(x => x.ConsumerId).NotEmpty();
        RuleFor(x => x.ActionKey).NotEmpty();
    }
}

public sealed class GetAllowanceStatusQueryHandler(
    IUsageLedgerRepository usageLedgers,
    IDebtLedgerRepository debtLedgers,
    IQuotaResolver quotaResolver,
    IRequestContextAccessor requestContext)
    : IRequestHandler<GetAllowanceStatusQuery, Result<AllowanceStatusDto>>
{
    public async Task<Result<AllowanceStatusDto>> Handle(GetAllowanceStatusQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;

        var consumerId = ConsumerId.Create(request.ConsumerId);
        if (consumerId.IsFailure)
            return Result<AllowanceStatusDto>.Failure(consumerId.Error);

        var actionKey = ActionKey.Create(request.ActionKey);
        if (actionKey.IsFailure)
            return Result<AllowanceStatusDto>.Failure(actionKey.Error);

        var now = DateTimeOffset.UtcNow;
        var candidateScopes = new List<SubscriptionScope>
        {
            SubscriptionScope.Create(PrincipalKind.User, request.ConsumerId).Value!,
            SubscriptionScope.Create(PrincipalKind.Tenant, tenantId).Value!
        };

        var quota = await quotaResolver.ResolveAsync(tenantId, candidateScopes, cancellationToken);
        if (quota.IsFailure)
            return Result<AllowanceStatusDto>.Failure(quota.Error);

        var usageLedger = await usageLedgers.GetByConsumerIdAsync(tenantId, consumerId.Value!, cancellationToken: cancellationToken);
        var debtLedger = await debtLedgers.GetByConsumerIdAsync(tenantId, consumerId.Value!, cancellationToken: cancellationToken);

        var windows = new List<WindowAllowanceDto>();
        foreach (var window in new[] { QuotaWindow.Daily, QuotaWindow.Weekly, QuotaWindow.Monthly })
        {
            var limit = quota.Value!.Quota.LimitFor(window);
            var used = usageLedger?.GetCount(actionKey.Value!, window, now) ?? 0;
            // Debt is a single (action) liability, not per window. It blocks ALL windows while > 0;
            // the per-window remaining allowance is simply limit minus usage (recovery already
            // deducted debt when computing the usable allowance at rollover).
            var hasDebt = (debtLedger?.OutstandingDebt(actionKey.Value!).Value ?? 0) > 0;
            var remaining = hasDebt ? 0 : System.Math.Max(0, limit - used);
            windows.Add(new WindowAllowanceDto(window.ToString(), limit, used, debtLedger?.OutstandingDebt(actionKey.Value!).Value ?? 0, remaining));
        }

        return Result<AllowanceStatusDto>.Success(new AllowanceStatusDto(windows.AsReadOnly()));
    }
}
