using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>Evaluation precedence of a policy. Lower rank wins when multiple policies apply.</summary>
public sealed class PolicyPriority : ValueObject
{
    private PolicyPriority(int rank) => Rank = rank;
    public int Rank { get; }
    public static Result<PolicyPriority> Create(int rank)
    {
        if (rank < 0)
            return Result<PolicyPriority>.Failure(PolicyErrors.PolicyConditionRequired);
        return Result<PolicyPriority>.Success(new PolicyPriority(rank));
    }
    public static PolicyPriority Default => new(100);
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Rank; }
    public override string ToString() => Rank.ToString();
}
