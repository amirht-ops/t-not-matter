using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.Errors;
using PolicyService.Domain.Services;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Administration;

/// <summary>
/// UC-25 — Administrative reset: clears usage + debt, never touches QuotaPolicy (invariant 10 /
/// ADR-018 §8). Structurally incapable of mutating configuration — no IQuotaPolicyRepository
/// dependency.
/// </summary>
public sealed record ResetAllowanceCommand(Guid ConsumerId)
    : IRequest<Result<Unit>>, ITransactionalRequest, IAuthorizableRequest, IIdempotentRequest
{
    public string IdempotencyKey => $"reset-allowance:{ConsumerId:N}";
    public string Action => "allowance.reset";
    public string Resource => $"consumer:{ConsumerId:N}";
}

public sealed class ResetAllowanceCommandValidator : AbstractValidator<ResetAllowanceCommand>
{
    public ResetAllowanceCommandValidator()
    {
        RuleFor(x => x.ConsumerId).NotEmpty();
    }
}

public sealed class ResetAllowanceCommandHandler(
    IUsageLedgerRepository usageLedgers,
    IDebtLedgerRepository debtLedgers,
    IAllowanceAdministrationService allowanceAdministration,
    IRequestContextAccessor requestContext)
    : IRequestHandler<ResetAllowanceCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(ResetAllowanceCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;

        var consumerId = ConsumerId.Create(command.ConsumerId);
        if (consumerId.IsFailure)
            return Result<Unit>.Failure(consumerId.Error);

        var usageLedger = await usageLedgers.GetOrCreateAsync(tenantId, consumerId.Value!, cancellationToken);
        if (usageLedger is null)
            return Result<Unit>.Failure(PolicyErrors.UsageLedgerNotFound);

        var debtLedger = await debtLedgers.GetOrCreateAsync(tenantId, consumerId.Value!, cancellationToken);
        if (debtLedger is null)
            return Result<Unit>.Failure(PolicyErrors.DebtLedgerNotFound);

        var reset = allowanceAdministration.Reset(usageLedger, debtLedger, context.CorrelationId);
        if (reset.IsFailure)
            return Result<Unit>.Failure(reset.Error);

        await usageLedgers.UpdateAsync(usageLedger, cancellationToken);
        await debtLedgers.UpdateAsync(debtLedger, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
