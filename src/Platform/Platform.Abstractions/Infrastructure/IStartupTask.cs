namespace Platform.Abstractions.Infrastructure;

public interface IStartupTask
{
    string DisplayName { get; }
    int Order => 0;
    Task ExecuteAsync(CancellationToken cancellationToken);
}
