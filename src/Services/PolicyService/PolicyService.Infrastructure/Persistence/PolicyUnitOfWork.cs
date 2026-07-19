using Platform.Abstractions.Infrastructure;

namespace PolicyService.Infrastructure.Persistence;

/// <summary>
/// Unit-of-Work for the PolicyService persistence context. Wraps a single EF Core transaction so
/// <see cref="UnitOfWorkBehavior"/> remains the sole transaction owner (ADR-015). One UoW per
/// DbContext, matching the AuthorizationService convention.
/// </summary>
public sealed class PolicyUnitOfWork(PolicyDbContext dbContext) : IPolicyUnitOfWork
{
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
            return;
        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => await dbContext.SaveChangesAsync(cancellationToken);

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
