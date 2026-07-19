using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.ActivateUser;

public sealed record ActivateUserCommand(Guid UserId, Guid? TargetTenantId = null) : IRequest<SKResult>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"activate-user:{UserId:N}";
    public string Action => "user.activate";
    public string Resource => $"user:{UserId:N}";
}
