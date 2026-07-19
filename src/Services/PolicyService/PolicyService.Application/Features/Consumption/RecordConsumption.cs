using System.Collections.Generic;
using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.Errors;
using PolicyService.Domain.Services;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Application;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Consumption;

/// <summary>
/// UC-20 — Record feed consumption (the core saga). The single hot-path, multi-aggregate command.
/// Marked transactional + idempotent + retryable, but NOT authorizable: it is a system/feed
/// operation authorized at the gateway/tenant context (ADR-013/018). The idempotency key is
/// supplied by the caller (the source feed event id) so at-least-once redelivery cannot
/// double-count usage.
/// </summary>
public sealed record RecordConsumptionCommand(Guid ConsumerId, string ActionKey, long Units, string IdempotencyKey)
    : IRequest<Result<RecordConsumptionResponse>>, ITransactionalRequest, IIdempotentRequest, IRetryableRequest
{
}

public sealed record RecordConsumptionResponse(ConsumptionDecisionType Decision, string? Reason);

public sealed class RecordConsumptionCommandValidator : AbstractValidator<RecordConsumptionCommand>
{
    public RecordConsumptionCommandValidator()
    {
        RuleFor(x => x.ConsumerId).NotEmpty();
        RuleFor(x => x.ActionKey).NotEmpty();
        RuleFor(x => x.Units).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}

public sealed class RecordConsumptionCommandHandler(
    ISubscriptionResolver subscriptionResolver,
    IQuotaResolver quotaResolver,
    IAllowanceEngine allowanceEngine,
    IUsageLedgerRepository usageLedgers,
    IDebtLedgerRepository debtLedgers,
    IRequestContextAccessor requestContext)
    : IRequestHandler<RecordConsumptionCommand, Result<RecordConsumptionResponse>>
{
    public async Task<Result<RecordConsumptionResponse>> Handle(RecordConsumptionCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;

        var consumerId = ConsumerId.Create(command.ConsumerId);
        if (consumerId.IsFailure)
            return Result<RecordConsumptionResponse>.Failure(consumerId.Error);

        var actionKey = ActionKey.Create(command.ActionKey);
        if (actionKey.IsFailure)
            return Result<RecordConsumptionResponse>.Failure(actionKey.Error);

        var units = ConsumedUnits.Create(command.Units);
        if (units.IsFailure)
            return Result<RecordConsumptionResponse>.Failure(units.Error);

        // Concurrency guard (Phase 8 fix): serialize record operations for the same consumer via a
        // transaction-scoped advisory lock. Without this, N simultaneous records for one consumer
        // race on the usage-ledger optimistic-concurrency (Version) CAS; only one wins per round and
        // the rest exhaust the bounded retry budget, silently dropping increments (lost updates)
        // while still returning success. The lock forces same-consumer writers to queue and each
        // observe the committed state of the previous one. It is released automatically when the
        // ambient UnitOfWork transaction commits or rolls back.
        await usageLedgers.AcquireConsumerLockAsync(tenantId, consumerId.Value!, cancellationToken);


        // ADR-014: candidate scope chain resolved against the local principal-hierarchy read model.
        // Until that read model is hydrated (P4/UC-26-29), fall back to [User, Tenant] scopes; the
        // Role scope is added once the read model provides the user→role edges.
        var candidateScopes = new List<SubscriptionScope>
        {
            SubscriptionScope.Create(PrincipalKind.User, command.ConsumerId).Value!,
            SubscriptionScope.Create(PrincipalKind.Tenant, tenantId).Value!
        };

        var subscription = await subscriptionResolver.ResolveAsync(tenantId, candidateScopes, cancellationToken);
        if (subscription.IsFailure)
            return Result<RecordConsumptionResponse>.Failure(subscription.Error);

        var quota = await quotaResolver.ResolveAsync(tenantId, candidateScopes, cancellationToken);
        if (quota.IsFailure)
            return Result<RecordConsumptionResponse>.Failure(quota.Error);

        var usageLedger = await usageLedgers.GetOrCreateAsync(tenantId, consumerId.Value!, cancellationToken);
        if (usageLedger is null)
            return Result<RecordConsumptionResponse>.Failure(PolicyErrors.UsageLedgerNotFound);

        var debtLedger = await debtLedgers.GetOrCreateAsync(tenantId, consumerId.Value!, cancellationToken);
        if (debtLedger is null)
            return Result<RecordConsumptionResponse>.Failure(PolicyErrors.DebtLedgerNotFound);

        var decision = allowanceEngine.Consume(usageLedger, debtLedger, quota.Value!.Quota, actionKey.Value!, units.Value!, context.CorrelationId);
        if (decision.IsFailure)
            return Result<RecordConsumptionResponse>.Failure(decision.Error);

        await usageLedgers.UpdateAsync(usageLedger, cancellationToken);
        await debtLedgers.UpdateAsync(debtLedger, cancellationToken);

        var result = decision.Value!;
        return Result<RecordConsumptionResponse>.Success(new RecordConsumptionResponse(result.Type, result.Reason));
    }
}
