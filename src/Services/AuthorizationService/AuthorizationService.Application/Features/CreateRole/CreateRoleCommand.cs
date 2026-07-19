using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.CreateRole;
public sealed record CreateRoleCommand(string Name, string? Description, Guid? ParentRoleId, Guid DepartmentId)
    : IRequest<Result<CreateRoleResponse>>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"create-role:{DepartmentId:N}:{Name}";
    public string Action => "role.create";
    public string Resource => $"role:{Name}";
}
