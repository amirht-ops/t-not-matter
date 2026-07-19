using SharedKernel.Domain.Guards;
using SharedKernel.Domain.Primitives;

namespace AuthorizationService.Domain.ValueObjects;

public sealed class PermissionId : ValueObject
{
    private PermissionId(Guid value) => Value = Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static PermissionId From(Guid value) => new(value);
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
