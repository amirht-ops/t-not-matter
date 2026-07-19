using IdentityService.Domain.Aggregates;

namespace IdentityService.Domain.Services;

/// <summary>
/// Pure domain service for authentication policy evaluation.
/// Delegates threshold evaluation to AccessRisk (the aggregate owns its own state evaluation).
/// This service exists for cases where cross-aggregate policy coordination is needed.
/// </summary>
public sealed class AuthenticationPolicy
{
    public bool ShouldLock(AccessRisk risk) => risk.ShouldLock();

    public bool ShouldReset(AccessRisk risk) => risk.ShouldReset();
}
