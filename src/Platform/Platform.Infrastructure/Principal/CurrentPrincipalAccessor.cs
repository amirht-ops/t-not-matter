using Platform.Abstractions.Principal;

namespace Platform.Infrastructure.Principal;

public sealed class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    public ICurrentPrincipal Principal { get; set; } = CurrentPrincipal.Unauthenticated;
}