using AuthorizationService.Domain.Aggregates.RoleAssignment;
using AuthorizationService.Domain.ValueObjects;

namespace AuthorizationService.Domain.Repositories;

public interface IRoleAssignmentRepository
{
    Task<IReadOnlyCollection<RoleAssignment>> GetActiveForSubjectAsync(Guid tenantId, SubjectId subjectId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Guid>> GetActiveRoleIdsIncludingInheritedAsync(Guid tenantId, SubjectId subjectId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetActiveRoleNamesForSubjectAsync(Guid tenantId, SubjectId subjectId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<RoleAssignment?> GetActiveAsync(Guid tenantId, SubjectId subjectId, Guid roleId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<SubjectId>> GetSubjectIdsByRoleAsync(Guid tenantId, Guid roleId, bool trackChanges = false, CancellationToken cancellationToken = default);
    Task AddAsync(RoleAssignment assignment, CancellationToken cancellationToken);
    Task UpdateAsync(RoleAssignment assignment, CancellationToken cancellationToken);
}
