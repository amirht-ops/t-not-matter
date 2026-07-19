using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;
using TenantService.Application.Features.GetDepartmentById;

namespace TenantService.Application.Features.ListDepartments;

public sealed record ListDepartmentsQuery(Guid TenantId) : IRequest<Result<IReadOnlyList<DepartmentDto>>>, IAuthorizableRequest, ICachedQuery
{
    public string Action => "department.list";
    public string Resource => $"tenant:{TenantId}:department:*";
    public string CacheKey => $"departments:{TenantId:N}";
    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(5);
}
