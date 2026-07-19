using SharedKernel.Domain.Guards;
using SharedKernel.Domain.Primitives;

namespace AuthorizationService.Domain.ValueObjects;

public sealed class SubjectId : ValueObject
{
    private SubjectId(Guid value, bool allowEmpty = false)
        => Value = allowEmpty ? value : Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static SubjectId Anonymous { get; } = new(Guid.Empty, allowEmpty: true);
    public static SubjectId From(Guid value) => new(value);
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
