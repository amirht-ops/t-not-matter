using PolicyService.Domain.Enums;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// A single (action, window) recovery marker owned by a <see cref="DebtLedger"/>.
/// Records how much of the renewed allowance remained after debt deduction at the last rollover
/// (<see cref="Position"/>), and the window boundary (<see cref="WindowEnd"/>) through which
/// recovery has been applied — used by lazy recovery to fire exactly once per period.
/// Debt itself is window-agnostic (see <see cref="DebtEntry"/>); this marker is only the
/// per-window post-repayment usable allowance.
/// Plain owned entity so it maps to a normalized table row under EF Core.
/// </summary>
public sealed class RecoveryEntry
{
    private RecoveryEntry() { }

    private RecoveryEntry(ActionKey action, QuotaWindow window, RecoveryPosition position, DateTimeOffset windowEnd)
    {
        Action = action;
        Window = window;
        Position = position;
        WindowEnd = windowEnd;
    }

    public ActionKey Action { get; private set; } = null!;
    public QuotaWindow Window { get; private set; }
    public RecoveryPosition Position { get; private set; } = null!;
    public DateTimeOffset WindowEnd { get; private set; }

    public static Result<RecoveryEntry> Create(ActionKey action, QuotaWindow window, long remaining, DateTimeOffset windowEnd)
        => Result<RecoveryEntry>.Success(new RecoveryEntry(action, window, RecoveryPosition.Create(remaining), windowEnd));
}
