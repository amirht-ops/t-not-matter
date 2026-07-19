using Platform.Abstractions.Locking;

namespace Platform.Caching.Locking;

public sealed class DistributedLock : IDistributedLock
{
    private readonly Func<DistributedLock, CancellationToken, Task> _releaseAction;
    private bool _disposed;

    public string Name { get; }
    public string LockId { get; }
    public bool IsAcquired { get; }

    public DistributedLock(string name, string lockId, Func<DistributedLock, CancellationToken, Task> releaseAction)
    {
        Name = name;
        LockId = lockId;
        IsAcquired = true;
        _releaseAction = releaseAction;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsAcquired)
        {
            await _releaseAction(this, CancellationToken.None);
        }
        GC.SuppressFinalize(this);
    }
}
