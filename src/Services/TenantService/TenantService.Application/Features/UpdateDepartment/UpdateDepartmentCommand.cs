using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKUnit = SharedKernel.Results.Unit;
using SharedKernel.Results;

namespace TenantService.Application.Features.UpdateDepartment;

public sealed record UpdateDepartmentCommand(
    Guid TenantId,
    Guid DepartmentId,
    string? Name,
    string? Description,
    Guid CorrelationId) : IRequest<Result<SKUnit>>, IAuthorizableRequest, ITransactionalRequest, IIdempotentRequest
{
    public string Action => "department.update";
    public string Resource => $"tenant:{TenantId}:department:{DepartmentId}";
    public string IdempotencyKey => $"update-department:{CorrelationId:N}";
}
