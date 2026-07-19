using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>The number of units a single consumption operation consumed. Always positive (invariant 3).</summary>
public sealed class ConsumedUnits : ValueObject
{
    private ConsumedUnits(long value) => Value = value;
    public long Value { get; }
    public static Result<ConsumedUnits> Create(long value)
    {
        if (value <= 0)
            return Result<ConsumedUnits>.Failure(PolicyErrors.ConsumedUnitsMustBePositive);
        return Result<ConsumedUnits>.Success(new ConsumedUnits(value));
    }
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
