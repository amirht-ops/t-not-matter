using IdentityService.Domain.Aggregates.User;
using IdentityService.Domain.ValueObjects;

namespace IdentityService.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid tenantId, Guid id, bool trackChanges = false, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAcrossTenantsAsync(Guid id, bool trackChanges = false, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameAsync(Username username, Guid tenantId, bool trackChanges = false,
        CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameUnSafeAsync(Username username, Guid tenantId, bool trackChanges = false,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByUsernameAsync(Username username, Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(Email email, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(Email email, Guid tenantId, CancellationToken cancellationToken = default);

    Task<User?> GetByPhoneNumberAsync(PhoneNumber phoneNumber, Guid tenantId, bool trackChanges = false,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByPhoneNumberAsync(PhoneNumber phoneNumber, Guid tenantId,
        CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task MoveToTenantAsync(User user, Guid oldTenantId, CancellationToken cancellationToken = default);
}
