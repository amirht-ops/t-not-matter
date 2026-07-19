using SharedKernel.Results;

namespace IdentityService.Application.Common.Abstractions;

public interface ITenantServiceClient
{
    Task<Result<bool>> ValidateDepartmentExistsAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default);
    Task<Result<Guid>> ResolveTenantIdBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
