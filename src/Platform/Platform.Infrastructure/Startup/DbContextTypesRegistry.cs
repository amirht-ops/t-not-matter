namespace Platform.Infrastructure.Startup;

public sealed class DbContextTypesRegistry
{
    private readonly List<Type> _types = new();

    public IReadOnlyList<Type> Types => _types.AsReadOnly();

    public void Register(Type dbContextType)
    {
        lock (_types)
        {
            if (!_types.Contains(dbContextType))
                _types.Add(dbContextType);
        }
    }
}
