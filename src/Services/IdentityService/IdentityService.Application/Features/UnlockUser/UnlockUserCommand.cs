using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.UnlockUser;

public sealed record UnlockUserCommand(Guid UserId, Guid? TargetTenantId = null) : IRequest<SKResult>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"unlock-user:{UserId:N}";
    public string Action => "user.unlock";
    public string Resource => $"user:{UserId:N}";
}
