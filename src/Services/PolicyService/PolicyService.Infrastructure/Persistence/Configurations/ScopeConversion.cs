using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;

namespace PolicyService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared persistence conversions for <see cref="SubscriptionScope"/>, which is stored as a single
/// "Kind:PrincipalId" column (its <see cref="SubscriptionScope.ToString"/> format).
/// </summary>
internal static class ScopeConversion
{
    public static string ToColumn(SubscriptionScope scope) => scope.ToString();

    public static SubscriptionScope FromColumn(string value)
    {
        var parts = value.Split(':', 2);
        var kind = Enum.Parse<PrincipalKind>(parts[0]);
        var principalId = Guid.Parse(parts[1]);
        return SubscriptionScope.Create(kind, principalId).Value!;
    }
}
