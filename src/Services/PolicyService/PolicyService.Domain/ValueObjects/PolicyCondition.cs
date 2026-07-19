using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// The immutable ABAC condition specification for a policy (e.g. attribute/operator/value
/// clauses). Stored as the canonical condition representation consumed by Rego generation.
/// </summary>
public sealed class PolicyCondition : ValueObject
{
    private PolicyCondition(string value) => Value = value;
    public string Value { get; }
    public static Result<PolicyCondition> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<PolicyCondition>.Failure(PolicyErrors.PolicyConditionRequired);
        return Result<PolicyCondition>.Success(new PolicyCondition(value));
    }
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}
