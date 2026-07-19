using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.ChangeUserRole;

public sealed record ChangeUserRoleCommand(Guid SubjectId, Guid NewRoleId, Guid AssignedBy) : IRequest<Result<ChangeUserRoleResponse>>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"change-user-role:{SubjectId:N}:{NewRoleId:N}";
    public string Action => "role.change";
    public string Resource => $"subject:{SubjectId:N}";
}
