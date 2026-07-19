using SharedKernel.Domain.Primitives;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// Remaining allowance after debt deduction at the last rollover. May be zero or negative
/// (negative meaning debt still exceeds renewed allowance; recovery continues).
/// </summary>
public sealed class RecoveryPosition : ValueObject
{
    private RecoveryPosition(long remaining) => Remaining = remaining;
    public long Remaining { get; }
    public static RecoveryPosition Create(long remaining) => new(remaining);
    public static RecoveryPosition Zero => new(0);
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Remaining; }
    public override string ToString() => Remaining.ToString();
}
