using Microsoft.EntityFrameworkCore.Storage;
using TenantService.Application.Common.Abstractions;

namespace TenantService.Infrastructure.Persistence;

public sealed class TenantUnitOfWork(TenantDbContext dbContext) : ITenantUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
            return;

        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
        => await dbContext.SaveChangesAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
            return;

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
            return;

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public void ClearChangeTracker() => dbContext.ChangeTracker.Clear();
}
