namespace Platform.Abstractions.Principal;

public interface ICurrentPrincipalAccessor
{
    ICurrentPrincipal Principal { get; set; }
}
