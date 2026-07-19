namespace Platform.Abstractions.Principal;

public interface IPlatformServiceRegistry
{
    bool IsKnownService(string serviceName);
    PlatformServiceName? Resolve(string serviceName);

    /// <summary>
    /// Normalizes a service name to its canonical PascalCase form.
    /// Handles backward-compatible legacy aliases derived from <see cref="PlatformServiceName.AllValues"/>.
    /// Returns null for unknown services (fail-closed).
    /// </summary>
    string? Normalize(string serviceName);

    IReadOnlyCollection<string> AllServiceNames { get; }
}