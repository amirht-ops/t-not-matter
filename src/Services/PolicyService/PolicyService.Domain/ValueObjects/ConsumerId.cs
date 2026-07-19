using SharedKernel.Domain.Guards;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// The entity that consumes feeds: a User (identified by id), per ADR-018 ubiquitous language.
/// Wraps the external identity reference only; PolicyService never owns the user.
/// </summary>
public sealed class ConsumerId : ValueObject
{
    private ConsumerId(Guid value) => Value = Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static ConsumerId From(Guid value) => new(value);
    public static Result<ConsumerId> Create(Guid value)
        => value == Guid.Empty
            ? Result<ConsumerId>.Failure(PolicyErrors.ConsumerIdRequired)
            : Result<ConsumerId>.Success(new ConsumerId(value));
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
