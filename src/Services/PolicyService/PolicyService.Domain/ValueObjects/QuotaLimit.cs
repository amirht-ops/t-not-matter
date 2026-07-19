using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>A single per-window allowance limit. Always non-negative (invariant 1).</summary>
public sealed class QuotaLimit : ValueObject
{
    private QuotaLimit(long value) => Value = value;
    public long Value { get; }
    public static Result<QuotaLimit> Create(long value)
    {
        if (value < 0)
            return Result<QuotaLimit>.Failure(PolicyErrors.QuotaLimitInvalid);
        return Result<QuotaLimit>.Success(new QuotaLimit(value));
    }
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
