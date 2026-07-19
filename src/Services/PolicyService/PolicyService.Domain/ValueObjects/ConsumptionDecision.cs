using PolicyService.Domain.Enums;
using SharedKernel.Domain.Primitives;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// The verdict of the Consumption Decision stage of the Policy Execution Pipeline (ADR-018 §20).
/// Consumption always succeeds (invariant 3); "Deny" here means blocked by outstanding debt
/// (invariant 6 / 7), never truncation.
/// </summary>
public sealed class ConsumptionDecision : ValueObject
{
    private ConsumptionDecision(ConsumptionDecisionType type, string? reason)
    {
        Type = type;
        Reason = reason;
    }

    public ConsumptionDecisionType Type { get; }
    public string? Reason { get; }
    public bool IsAllowed => Type == ConsumptionDecisionType.Allow;

    public static ConsumptionDecision Allow() => new(ConsumptionDecisionType.Allow, null);
    public static ConsumptionDecision Deny(string reason) => new(ConsumptionDecisionType.Deny, reason);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Reason;
    }
}
