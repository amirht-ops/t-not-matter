using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace IdentityService.Application.Features.GetUser;

public sealed record GetUserQuery(Guid UserId) : IRequest<Result<GetUserResponse>>, ICachedQuery, IAuthorizableRequest
{
    public string CacheKey => $"user:{UserId:N}";
    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(5);
    public string Action => "user.read";
    public string Resource => $"user:{UserId:N}";
}
