using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.DeleteUser;

public sealed record DeleteUserCommand(Guid UserId, Guid? TargetTenantId = null) : IRequest<SKResult>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"delete-user:{UserId:N}";
    public string Action => "user.delete";
    public string Resource => $"user:{UserId:N}";
}
