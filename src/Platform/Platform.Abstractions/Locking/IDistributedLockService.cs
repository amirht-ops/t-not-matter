namespace Platform.Abstractions.Locking;

public interface IDistributedLockService
{
    Task<IDistributedLock?> AcquireLockAsync(string lockName, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task ReleaseLockAsync(IDistributedLock distributedLock, CancellationToken cancellationToken = default);
}

public interface IDistributedLock : IAsyncDisposable
{
    string Name { get; }
    string LockId { get; }
    bool IsAcquired { get; }
}
