using System.Security.Claims;

namespace Platform.Abstractions.Principal;

public interface ICurrentPrincipalFactory
{
    ICurrentPrincipal CreateFromClaimsPrincipal(ClaimsPrincipal claimsPrincipal);
    ICurrentPrincipal CreateServicePrincipal(PlatformServiceName service);
    ICurrentPrincipal CreateSystemPrincipal();
    ICurrentPrincipal CreateUnauthenticated();
}