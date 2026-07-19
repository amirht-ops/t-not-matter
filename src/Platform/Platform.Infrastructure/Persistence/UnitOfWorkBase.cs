using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Persistence;

public abstract class UnitOfWorkBase<TDbContext>(TDbContext context)
    : ITransactionalUnitOfWork
    where TDbContext : DbContext
{
    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        _transaction = await context.Database.BeginTransactionAsync(ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return await context.SaveChangesAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct)
    {
        if (_transaction is not null)
            await _transaction.CommitAsync(ct);
    }

    public async Task RollbackTransactionAsync(CancellationToken ct)
    {
        if (_transaction is not null)
            await _transaction.RollbackAsync(ct);
    }

    public void ClearChangeTracker() => context.ChangeTracker.Clear();

    public void Dispose()
    {
        _transaction?.Dispose();
    }
}
