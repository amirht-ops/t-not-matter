using Microsoft.EntityFrameworkCore;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using PolicyService.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Repositories;

public sealed class DebtLedgerRepository(PolicyDbContext dbContext) : IDebtLedgerRepository
{
    public Task<DebtLedger?> GetByIdAsync(Guid tenantId, DebtLedgerId debtLedgerId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        trackChanges
            ? dbContext.DebtLedgers.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == debtLedgerId.Value, cancellationToken)
            : dbContext.DebtLedgers.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == debtLedgerId.Value, cancellationToken);

    public Task<DebtLedger?> GetByConsumerIdAsync(Guid tenantId, ConsumerId consumerId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
        trackChanges
            ? dbContext.DebtLedgers.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.ConsumerId == consumerId, cancellationToken)
            : dbContext.DebtLedgers.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == tenantId && l.ConsumerId == consumerId, cancellationToken);

    public async Task<DebtLedger?> GetOrCreateAsync(Guid tenantId, ConsumerId consumerId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.DebtLedgers.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.ConsumerId == consumerId, cancellationToken);
        if (existing is not null)
            return existing;

        var created = DebtLedger.Create(tenantId, consumerId);
        if (created.IsFailure)
            return null;

        dbContext.DebtLedgers.Add(created.Value!);
        return created.Value!;
    }

    public Task UpdateAsync(DebtLedger debtLedger, CancellationToken cancellationToken) => Task.CompletedTask;
}
