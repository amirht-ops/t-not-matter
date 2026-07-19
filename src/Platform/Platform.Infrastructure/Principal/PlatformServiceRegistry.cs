using Platform.Abstractions.Principal;

namespace Platform.Infrastructure.Principal;

public sealed class PlatformServiceRegistry : IPlatformServiceRegistry
{
    public bool IsKnownService(string serviceName) =>
        PlatformServiceName.IsKnown(serviceName);

    public PlatformServiceName? Resolve(string serviceName) =>
        PlatformServiceName.Resolve(serviceName);

    public string? Normalize(string serviceName) =>
        PlatformServiceName.Resolve(serviceName)?.Value;

    public IReadOnlyCollection<string> AllServiceNames => PlatformServiceName.AllValues;
}