using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace AuthorizationService.Application.Features.RevokeRole;

public sealed record RevokeRoleCommand(Guid SubjectId, Guid RoleId) : IRequest<Result<Unit>>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"revoke-role:{SubjectId:N}:{RoleId:N}";
    public string Action => "role.revoke";
    public string Resource => $"subject:{SubjectId:N}";
}
