using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.GrantPermission;

public sealed record GrantPermissionCommand(Guid RoleId, Guid PermissionId, Guid GrantedBy) : IRequest<Result<GrantPermissionResponse>>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"grant-permission:{RoleId:N}:{PermissionId:N}";
    public string Action => "permission.grant";
    public string Resource => $"role:{RoleId:N}";
}
