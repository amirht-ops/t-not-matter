using PolicyService.Domain.Enums;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// The scope a policy / quota policy is defined against: a Tenant, Role, or User principal.
/// Holds the principal kind + external id reference only (no cross-BC type).
/// </summary>
public sealed class SubscriptionScope : ValueObject
{
    private SubscriptionScope(PrincipalKind kind, Guid principalId)
    {
        Kind = kind;
        PrincipalId = principalId;
    }

    public PrincipalKind Kind { get; }
    public Guid PrincipalId { get; }

    public static Result<SubscriptionScope> Create(PrincipalKind kind, Guid principalId)
    {
        if (!Enum.IsDefined(kind))
            return Result<SubscriptionScope>.Failure(PolicyErrors.ScopeKindInvalid);
        if (principalId == Guid.Empty)
            return Result<SubscriptionScope>.Failure(PolicyErrors.ScopePrincipalRequired);
        return Result<SubscriptionScope>.Success(new SubscriptionScope(kind, principalId));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return PrincipalId;
    }

    public override string ToString() => $"{Kind}:{PrincipalId}";
}
