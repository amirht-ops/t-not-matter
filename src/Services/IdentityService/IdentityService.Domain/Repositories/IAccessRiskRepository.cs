using IdentityService.Domain.Aggregates;

namespace IdentityService.Domain.Repositories;

public interface IAccessRiskRepository
{
    Task<AccessRisk?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task AddAsync(AccessRisk accessRisk, CancellationToken cancellationToken);
    Task UpdateAsync(AccessRisk accessRisk, CancellationToken cancellationToken);
}
