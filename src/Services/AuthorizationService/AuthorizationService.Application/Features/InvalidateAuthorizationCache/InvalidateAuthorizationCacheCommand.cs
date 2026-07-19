using MediatR;
using SharedKernel.Authorization;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace AuthorizationService.Application.Features.InvalidateAuthorizationCache;

public sealed record InvalidateAuthorizationCacheCommand(Guid SubjectId) : IRequest<Result<Unit>>, IAuthorizableRequest
{
    public string Action => "cache.invalidate";
    public string Resource => "authorization:cache";
}
