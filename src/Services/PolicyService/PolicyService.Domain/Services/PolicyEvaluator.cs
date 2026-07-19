using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>Evaluates ABAC conditions for a policy. The definitive evaluation is delegated to OPA;
/// the domain verdict is Allow (gating is performed by consumption/quota/debt, not by ABAC truncation).</summary>
public sealed class PolicyEvaluator : IPolicyEvaluator
{
    public Result<ConsumptionDecision> Evaluate(PolicyExpression expression, PolicyCondition condition)
    {
        if (expression is null || condition is null)
            return Result<ConsumptionDecision>.Failure(PolicyErrors.PolicyExpressionRequired);

        return Result<ConsumptionDecision>.Success(ConsumptionDecision.Allow());
    }
}
