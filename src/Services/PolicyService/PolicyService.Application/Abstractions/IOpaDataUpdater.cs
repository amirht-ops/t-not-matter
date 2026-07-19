namespace PolicyService.Application.Abstractions;

public interface IOpaDataUpdater
{
    Task SyncPolicyDataAsync(string documentPath, object? data, CancellationToken cancellationToken);
}
