using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>Outstanding business debt. Never negative (invariant 2). A separate concept from usage.</summary>
public sealed class DebtAmount : ValueObject
{
    private DebtAmount(long value) => Value = value;
    public long Value { get; }

    public static DebtAmount Zero => new(0);

    public static Result<DebtAmount> Create(long value)
    {
        if (value < 0)
            return Result<DebtAmount>.Failure(PolicyErrors.DebtAmountInvalid);
        return Result<DebtAmount>.Success(new DebtAmount(value));
    }

    public DebtAmount Add(DebtAmount other) => new(Value + other.Value);
    public DebtAmount Subtract(DebtAmount other) => new(Math.Max(0, Value - other.Value));

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
