using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace TenantService.Application.Features.CreateDepartment;

public sealed record CreateDepartmentCommand(
    Guid TenantId,
    string Name,
    string? Description,
    Guid CorrelationId) : IRequest<Result<CreateDepartmentResponse>>, IAuthorizableRequest, ITransactionalRequest, IIdempotentRequest
{
    public string Action => "department.create";
    public string Resource => $"tenant:{TenantId}:department:*";
    public string IdempotencyKey => $"create-department:{CorrelationId:N}";
}

public sealed record CreateDepartmentResponse(Guid DepartmentId);
