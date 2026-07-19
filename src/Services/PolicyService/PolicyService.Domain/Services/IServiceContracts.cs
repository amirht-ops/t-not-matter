using System.Threading;
using System.Threading.Tasks;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Aggregates.UsageLedgers;
using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>Generates a compiled Rego module from a policy expression + condition (ADR-003).</summary>
public interface IRegoGenerationService
{
    Result<RegoModule> Generate(PolicyExpression expression, PolicyCondition condition);
}

/// <summary>Compiles a policy expression into a Rego module.</summary>
public interface IPolicyCompiler
{
    Result<RegoModule> Compile(PolicyExpression expression);
}

/// <summary>Evaluates ABAC conditions for a policy (pure; OPA consults the produced Rego).</summary>
public interface IPolicyEvaluator
{
    Result<ConsumptionDecision> Evaluate(PolicyExpression expression, PolicyCondition condition);
}

/// <summary>Resolves the effective Subscription for a scope chain (Pipeline stage 1).</summary>
public interface ISubscriptionResolver
{
    Task<Result<Subscription>> ResolveAsync(Guid tenantId, IReadOnlyCollection<SubscriptionScope> candidateScopes, CancellationToken cancellationToken = default);
}

/// <summary>Resolves the effective QuotaPolicy for a scope chain (Pipeline stage 3).</summary>
public interface IQuotaResolver
{
    Task<Result<QuotaPolicy>> ResolveAsync(Guid tenantId, IReadOnlyCollection<SubscriptionScope> candidateScopes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates a consumption: debt resolution → decision → usage recording → debt update
/// (Pipeline stages 4–7). Pure: receives the already-loaded ledgers + resolved quota, mutates
/// them, raises events. The caller (handler) persists in one transaction (ADR-015).
/// </summary>
public interface IAllowanceEngine
{
    Result<ConsumptionDecision> Consume(
        UsageLedger usageLedger,
        DebtLedger debtLedger,
        Quota effectiveQuota,
        ActionKey actionKey,
        ConsumedUnits units,
        Guid correlationId);
}

/// <summary>Applies debt-first recovery on a window renewal event (Pipeline: Recovery stage).</summary>
public interface IRecoveryProcessor
{
    Result<Unit> Recover(
        DebtLedger debtLedger,
        ActionKey actionKey,
        QuotaWindow window,
        long renewedAllowance,
        DateTimeOffset boundaryEnd,
        Guid correlationId);
}

/// <summary>Executes administrative Reset (clears usage + debt; never touches QuotaPolicy).</summary>
public interface IAllowanceAdministrationService
{
    Result<Unit> Reset(UsageLedger usageLedger, DebtLedger debtLedger, Guid correlationId);
}
