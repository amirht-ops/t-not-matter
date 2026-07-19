using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

public sealed class ActionKey : ValueObject
{
    private ActionKey(string value) => Value = value;
    public string Value { get; }
    public static Result<ActionKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<ActionKey>.Failure(PolicyErrors.ActionKeyRequired);
        return Result<ActionKey>.Success(new ActionKey(value.Trim()));
    }
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}
