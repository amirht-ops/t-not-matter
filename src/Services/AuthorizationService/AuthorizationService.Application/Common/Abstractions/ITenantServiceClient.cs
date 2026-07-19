using SharedKernel.Results;

namespace AuthorizationService.Application.Common.Abstractions;

public interface ITenantServiceClient
{
    Task<Result<bool>> ValidateDepartmentExistsAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default);
}