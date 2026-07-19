using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.DisableUser;

public sealed record DisableUserCommand(Guid UserId, Guid? TargetTenantId = null) : IRequest<SKResult>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"disable-user:{UserId:N}";
    public string Action => "user.disable";
    public string Resource => $"user:{UserId:N}";
}
