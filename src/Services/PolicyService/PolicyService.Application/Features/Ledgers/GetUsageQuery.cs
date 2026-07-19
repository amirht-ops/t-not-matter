using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Ledgers;

public sealed record UsageDto(long Count);

/// <summary>UC-21 — Get usage count for a consumer/action/window. Direct ledger read, no cache (high-churn).</summary>
public sealed record GetUsageQuery(Guid ConsumerId, string ActionKey, QuotaWindow Window)
    : IRequest<Result<UsageDto>>
{
}

public sealed class GetUsageQueryValidator : AbstractValidator<GetUsageQuery>
{
    public GetUsageQueryValidator()
    {
        RuleFor(x => x.ConsumerId).NotEmpty();
        RuleFor(x => x.ActionKey).NotEmpty();
    }
}

public sealed class GetUsageQueryHandler(IUsageLedgerRepository usageLedgers, IRequestContextAccessor requestContext)
    : IRequestHandler<GetUsageQuery, Result<UsageDto>>
{
    public async Task<Result<UsageDto>> Handle(GetUsageQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var consumerId = ConsumerId.Create(request.ConsumerId);
        if (consumerId.IsFailure)
            return Result<UsageDto>.Failure(consumerId.Error);

        var actionKey = ActionKey.Create(request.ActionKey);
        if (actionKey.IsFailure)
            return Result<UsageDto>.Failure(actionKey.Error);

        var ledger = await usageLedgers.GetByConsumerIdAsync(context.TenantId, consumerId.Value!, cancellationToken: cancellationToken);
        var count = ledger?.GetCount(actionKey.Value!, request.Window, DateTimeOffset.UtcNow) ?? 0;

        return Result<UsageDto>.Success(new UsageDto(count));
    }
}
