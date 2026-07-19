namespace Platform.Abstractions.Infrastructure;

public interface ITransactionalUnitOfWork : IUnitOfWork
{
    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitTransactionAsync(CancellationToken ct);
    Task RollbackTransactionAsync(CancellationToken ct);
    void ClearChangeTracker();
}
