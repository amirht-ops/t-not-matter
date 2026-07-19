using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>Generates a compiled Rego module from a policy expression + ABAC condition (ADR-003).</summary>
/// <remarks>Quota/debt thresholds are intentionally NOT encoded here (ADR-018 §15 rejected).</remarks>
public sealed class RegoGenerationService : IRegoGenerationService
{
    public Result<RegoModule> Generate(PolicyExpression expression, PolicyCondition condition)
    {
        if (expression is null || condition is null)
            return Result<RegoModule>.Failure(PolicyErrors.RegoSourceRequired);

        var source = $"package policy\n\n# condition: {condition.Value}\n{expression.Value}";
        return RegoModule.Create(source);
    }
}
