using SharedKernel.Domain.Guards;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

public sealed class PolicyId : ValueObject
{
    private PolicyId(Guid value) => Value = Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static PolicyId From(Guid value) => new(value);
    public static Result<PolicyId> Create(Guid value)
        => value == Guid.Empty
            ? Result<PolicyId>.Failure(PolicyErrors.PolicyNotFound)
            : Result<PolicyId>.Success(new PolicyId(value));
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}

public sealed class SubscriptionId : ValueObject
{
    private SubscriptionId(Guid value) => Value = Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static SubscriptionId From(Guid value) => new(value);
    public static Result<SubscriptionId> Create(Guid value)
        => value == Guid.Empty
            ? Result<SubscriptionId>.Failure(PolicyErrors.SubscriptionNotFound)
            : Result<SubscriptionId>.Success(new SubscriptionId(value));
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}

public sealed class QuotaPolicyId : ValueObject
{
    private QuotaPolicyId(Guid value) => Value = Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static QuotaPolicyId From(Guid value) => new(value);
    public static Result<QuotaPolicyId> Create(Guid value)
        => value == Guid.Empty
            ? Result<QuotaPolicyId>.Failure(PolicyErrors.QuotaPolicyNotFound)
            : Result<QuotaPolicyId>.Success(new QuotaPolicyId(value));
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}

public sealed class UsageLedgerId : ValueObject
{
    private UsageLedgerId(Guid value) => Value = Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static UsageLedgerId From(Guid value) => new(value);
    public static Result<UsageLedgerId> Create(Guid value)
        => value == Guid.Empty
            ? Result<UsageLedgerId>.Failure(PolicyErrors.UsageLedgerNotFound)
            : Result<UsageLedgerId>.Success(new UsageLedgerId(value));
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}

public sealed class DebtLedgerId : ValueObject
{
    private DebtLedgerId(Guid value) => Value = Guard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public static DebtLedgerId From(Guid value) => new(value);
    public static Result<DebtLedgerId> Create(Guid value)
        => value == Guid.Empty
            ? Result<DebtLedgerId>.Failure(PolicyErrors.DebtLedgerNotFound)
            : Result<DebtLedgerId>.Success(new DebtLedgerId(value));
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
