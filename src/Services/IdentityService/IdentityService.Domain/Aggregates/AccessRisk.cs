using SharedKernel.Domain.Primitives;

namespace IdentityService.Domain.Aggregates;

/// <summary>
/// High-frequency authentication risk telemetry. Independent persistence boundary.
/// Does NOT use optimistic concurrency (Version) — see ADR-008 Amendment 1.
/// </summary>
public sealed class AccessRisk : AggregateRoot
{
    private const int LockThreshold = 5;

    /// <summary>
    /// EF Core constructor. Uses Id (from base) as the UserId storage key.
    /// </summary>
    private AccessRisk(Guid id, Guid tenantId) : base(id, tenantId)
    {
        FailedCount = 0;
    }

    /// <summary>
    /// Strongly-typed identity. This aggregate is a 1:1 extension of a User's
    /// authentication risk state — UserId IS the aggregate identity.
    /// </summary>
    public Guid UserId => Id;

    public int FailedCount { get; private set; }
    public DateTimeOffset? LastFailureAt { get; private set; }

    /// <summary>
    /// Factory for new users with no prior failure history.
    /// </summary>
    public static AccessRisk Create(Guid userId, Guid tenantId) => new(userId, tenantId);

    /// <summary>
    /// Records a failed authentication attempt.
    /// </summary>
    public void RecordFailure(DateTimeOffset occurredAt)
    {
        FailedCount++;
        LastFailureAt = occurredAt;
        MarkUpdated();
    }

    /// <summary>
    /// Resets failure counter after a successful authentication.
    /// </summary>
    public void Reset()
    {
        if (FailedCount == 0 && LastFailureAt == null) return;

        FailedCount = 0;
        LastFailureAt = null;
        MarkUpdated();
    }

    /// <summary>
    /// Evaluates whether this user should be locked out based on failure count.
    /// Business rule: lock after {LockThreshold} consecutive failures.
    /// </summary>
    public bool ShouldLock() => FailedCount >= LockThreshold;

    /// <summary>
    /// Evaluates whether the failure counter should be reset after a successful login.
    /// Business rule: reset if there are any recorded failures to clear.
    /// </summary>
    public bool ShouldReset() => FailedCount > 0 || LastFailureAt != null;
}
