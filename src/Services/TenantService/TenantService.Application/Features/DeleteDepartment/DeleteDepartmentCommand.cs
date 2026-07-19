using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKUnit = SharedKernel.Results.Unit;
using SharedKernel.Results;

namespace TenantService.Application.Features.DeleteDepartment;

public sealed record DeleteDepartmentCommand(
    Guid TenantId,
    Guid DepartmentId,
    Guid CorrelationId) : IRequest<Result<SKUnit>>, IAuthorizableRequest, ITransactionalRequest, IIdempotentRequest
{
    public string Action => "department.delete";
    public string Resource => $"tenant:{TenantId}:department:{DepartmentId}";
    public string IdempotencyKey => $"delete-department:{CorrelationId:N}";
}
