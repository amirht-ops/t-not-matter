using SharedKernel.Domain.Guards;
using SharedKernel.Domain.Primitives;

namespace AuthorizationService.Domain.ValueObjects;

public sealed class AuthorizationAction : ValueObject
{
    private AuthorizationAction(string value) => Value = Guard.NotEmpty(value, nameof(value));
    public string Value { get; }
    public static AuthorizationAction Create(string value) => new(value);
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value.ToUpperInvariant(); }
    public override string ToString() => Value;
}
