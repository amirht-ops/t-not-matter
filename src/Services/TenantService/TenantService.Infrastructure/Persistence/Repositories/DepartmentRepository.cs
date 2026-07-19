using Microsoft.EntityFrameworkCore;
using TenantService.Domain.Aggregates;
using TenantService.Domain.Repositories;
using TenantService.Domain.ValueObjects;

namespace TenantService.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository(TenantDbContext context) : IDepartmentRepository
{
    public Task<Department?> GetByIdAsync(Guid tenantId, Guid id, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = context.Departments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken);
    }

    public Task<Department?> GetByNameAsync(Guid tenantId, string name, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var nameResult = DepartmentName.Create(name);
        if (nameResult.IsFailure) return Task.FromResult<Department?>(null);
        var departmentName = nameResult.Value!;

        var query = context.Departments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Name == departmentName, cancellationToken);
    }

    public Task<IReadOnlyList<Department>> GetByTenantIdAsync(Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = context.Departments.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken)
            .ContinueWith(t => (IReadOnlyList<Department>)t.Result, cancellationToken);
    }

    public Task<bool> ExistsByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
    {
        var nameResult = DepartmentName.Create(name);
        if (nameResult.IsFailure) return Task.FromResult(false);
        var departmentName = nameResult.Value!;

        return context.Departments
            .AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.Name == departmentName, cancellationToken);
    }

    public async Task AddAsync(Department department, CancellationToken cancellationToken = default)
    {
        await context.Departments.AddAsync(department, cancellationToken);
    }

    public Task UpdateAsync(Department department, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
