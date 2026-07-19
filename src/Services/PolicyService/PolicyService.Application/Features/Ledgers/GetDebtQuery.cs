using System.Linq;
using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Ledgers;

public sealed record DebtDto(string Action, long Amount);

public sealed record DebtSummaryDto(IReadOnlyCollection<DebtDto> Debts);

/// <summary>UC-22 — Get outstanding debt for a consumer. Direct ledger read, no cache (high-churn).</summary>
public sealed record GetDebtQuery(Guid ConsumerId, string? ActionKey = null)
    : IRequest<Result<DebtSummaryDto>>
{
}

public sealed class GetDebtQueryValidator : AbstractValidator<GetDebtQuery>
{
    public GetDebtQueryValidator()
    {
        RuleFor(x => x.ConsumerId).NotEmpty();
    }
}

public sealed class GetDebtQueryHandler(IDebtLedgerRepository debtLedgers, IRequestContextAccessor requestContext)
    : IRequestHandler<GetDebtQuery, Result<DebtSummaryDto>>
{
    public async Task<Result<DebtSummaryDto>> Handle(GetDebtQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var consumerId = ConsumerId.Create(request.ConsumerId);
        if (consumerId.IsFailure)
            return Result<DebtSummaryDto>.Failure(consumerId.Error);

        var ledger = await debtLedgers.GetByConsumerIdAsync(context.TenantId, consumerId.Value!, cancellationToken: cancellationToken);
        if (ledger is null)
            return Result<DebtSummaryDto>.Success(new DebtSummaryDto([]));

        var debts = ledger.Debts
            .Where(d => request.ActionKey is null || d.Action.Value == request.ActionKey)
            .Select(d => new DebtDto(d.Action.Value, d.Amount.Value))
            .ToList();

        return Result<DebtSummaryDto>.Success(new DebtSummaryDto(debts.AsReadOnly()));
    }
}
