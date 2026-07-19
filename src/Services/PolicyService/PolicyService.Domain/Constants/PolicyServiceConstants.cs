namespace PolicyService.Domain.Constants;

public static class PolicyServiceConstants
{
    public const string DomainName = "policy";
    public const string EventVersion = "v1";
    public const string EventTypePrefix = "policy";

    /// <summary>Hierarchy resolution order (highest precedence first).</summary>
    public static readonly IReadOnlyList<Enums.PrincipalKind> ResolutionOrder =
        new[] { Enums.PrincipalKind.User, Enums.PrincipalKind.Role, Enums.PrincipalKind.Tenant };
}
