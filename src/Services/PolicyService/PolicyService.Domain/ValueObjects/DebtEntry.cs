using PolicyService.Domain.Enums;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// A single (action) outstanding debt position owned by a <see cref="DebtLedger"/>.
/// Debt is NOT attached to a quota window (business rule): it is one outstanding liability
/// per (consumer, action), denominated in allowance units, repaid by any window's renewal.
/// Plain owned entity so it maps to a normalized table row under EF Core.
/// </summary>
public sealed class DebtEntry
{
    private DebtEntry() { }

    private DebtEntry(ActionKey action, DebtAmount amount)
    {
        Action = action;
        Amount = amount;
    }

    public ActionKey Action { get; private set; } = null!;
    public DebtAmount Amount { get; private set; } = null!;

    public static Result<DebtEntry> Create(ActionKey action, long amount)
    {
        if (action is null)
            return Result<DebtEntry>.Failure(PolicyErrors.ActionKeyRequired);

        var debt = DebtAmount.Create(amount);
        if (debt.IsFailure)
            return Result<DebtEntry>.Failure(debt.Error);

        return Result<DebtEntry>.Success(new DebtEntry(action, debt.Value!));
    }

    public DebtEntry WithAmount(DebtAmount amount) => new(Action, amount);
}
