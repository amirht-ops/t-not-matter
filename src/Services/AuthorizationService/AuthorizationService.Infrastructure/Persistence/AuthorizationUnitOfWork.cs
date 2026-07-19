using AuthorizationService.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace AuthorizationService.Infrastructure.Persistence;

public sealed class AuthorizationUnitOfWork(AuthorizationDbContext dbContext) : IAuthorizationUnitOfWork
{
    private IDbContextTransaction? transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null) return;
        transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
        => await dbContext.SaveChangesAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is null) return;
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is null) return;
        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public void ClearChangeTracker() => dbContext.ChangeTracker.Clear();
}
