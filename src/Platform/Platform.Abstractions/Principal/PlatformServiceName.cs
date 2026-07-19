namespace Platform.Abstractions.Principal;

public sealed record PlatformServiceName
{
    public static readonly PlatformServiceName Identity = new("Identity");
    public static readonly PlatformServiceName Tenant = new("Tenant");
    public static readonly PlatformServiceName Authorization = new("Authorization");
    public static readonly PlatformServiceName Audit = new("Audit");
    public static readonly PlatformServiceName Policy = new("Policy");
    public static readonly PlatformServiceName Scheduler = new("Scheduler");
    public static readonly PlatformServiceName Opa = new("OPA");
    public static readonly PlatformServiceName Notification = new("Notification");

    private static readonly Dictionary<string, PlatformServiceName> All = BuildAliasMap();

    public string Value { get; }

    private PlatformServiceName(string value) => Value = value;

    private static Dictionary<string, PlatformServiceName> BuildAliasMap()
    {
        var services = new[]
        {
            Identity,
            Tenant,
            Authorization,
            Audit,
            Policy,
            Scheduler,
            Opa,
            Notification,
        };

        var map = new Dictionary<string, PlatformServiceName>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in services)
        {
            map[service.Value] = service;
            map[$"{service.Value.ToLowerInvariant()}-service"] = service;
        }

        return map;
    }

    public static bool IsKnown(string value) => All.ContainsKey(value);

    public static PlatformServiceName? Resolve(string value) =>
        All.TryGetValue(value, out var service) ? service : null;

    public static IReadOnlyCollection<string> AllValues => All.Keys;
}