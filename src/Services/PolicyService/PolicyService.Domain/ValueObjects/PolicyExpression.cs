using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>The Rego expression (ABAC rule body) carried by a policy. Immutable.</summary>
public sealed class PolicyExpression : ValueObject
{
    private PolicyExpression(string value) => Value = value;
    public string Value { get; }
    public static Result<PolicyExpression> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<PolicyExpression>.Failure(PolicyErrors.PolicyExpressionRequired);
        return Result<PolicyExpression>.Success(new PolicyExpression(value));
    }
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}
