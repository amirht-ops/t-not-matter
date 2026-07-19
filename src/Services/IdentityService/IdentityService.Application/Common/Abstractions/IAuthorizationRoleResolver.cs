using SharedKernel.Results;

namespace IdentityService.Application.Common.Abstractions;

public interface IAuthorizationRoleResolver
{
    Task<Result<ActiveRoleInfo>> GetActiveRoleForUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}

public sealed record ActiveRoleInfo(Guid? RoleId, Guid? DepartmentId);
