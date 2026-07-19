using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.AssignRole;

public sealed record AssignRoleCommand(Guid SubjectId, Guid RoleId, Guid AssignedBy) : IRequest<Result<AssignRoleResponse>>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"assign-role:{SubjectId:N}:{RoleId:N}";
    public string Action => "role.assign";
    public string Resource => $"subject:{SubjectId:N}";
}
