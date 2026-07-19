using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace AuthorizationService.Application.Features.RevokePermission;

public sealed record RevokePermissionCommand(Guid RoleId, Guid PermissionId) : IRequest<Result<Unit>>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"revoke-permission:{RoleId:N}:{PermissionId:N}";
    public string Action => "permission.revoke";
    public string Resource => $"role:{RoleId:N}";
}
