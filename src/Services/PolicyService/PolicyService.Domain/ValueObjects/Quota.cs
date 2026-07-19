using PolicyService.Domain.Enums;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// The per-window allowance limits. A Value Object (no identity/lifecycle of its own),
/// owned by the <see cref="QuotaPolicy"/> aggregate (ADR-018 §7).
/// </summary>
public sealed class Quota : ValueObject
{
    private Quota(QuotaLimit daily, QuotaLimit weekly, QuotaLimit monthly)
    {
        Daily = daily;
        Weekly = weekly;
        Monthly = monthly;
    }

    public QuotaLimit Daily { get; }
    public QuotaLimit Weekly { get; }
    public QuotaLimit Monthly { get; }

    public static Result<Quota> Create(QuotaLimit daily, QuotaLimit weekly, QuotaLimit monthly)
    {
        // Linked windows: monthly dominates weekly dominates daily (invariant 7, aggregate report).
        if (monthly.Value < weekly.Value || weekly.Value < daily.Value)
            return Result<Quota>.Failure(PolicyErrors.QuotaWindowInconsistent);

        return Result<Quota>.Success(new Quota(daily, weekly, monthly));
    }

    public long LimitFor(QuotaWindow window) => window switch
    {
        QuotaWindow.Daily => Daily.Value,
        QuotaWindow.Weekly => Weekly.Value,
        QuotaWindow.Monthly => Monthly.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(window))
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Daily;
        yield return Weekly;
        yield return Monthly;
    }
}
