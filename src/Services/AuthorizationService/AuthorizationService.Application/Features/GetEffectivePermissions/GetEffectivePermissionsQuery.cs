using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.GetEffectivePermissions;

public sealed record GetEffectivePermissionsQuery(Guid SubjectId) : IRequest<Result<GetEffectivePermissionsResponse>>, ICachedQuery, IAuthorizableRequest
{
    public string CacheKey => $"effective-permissions:{SubjectId:N}";
    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(5);
    public string Action => "permission.effective.read";
    public string Resource => $"subject:{SubjectId:N}";
}
