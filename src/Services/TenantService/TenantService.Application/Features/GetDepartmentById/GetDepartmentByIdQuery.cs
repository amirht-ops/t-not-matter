using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace TenantService.Application.Features.GetDepartmentById;

public sealed record GetDepartmentByIdQuery(
    Guid TenantId,
    Guid DepartmentId) : IRequest<Result<DepartmentDto>>, IAuthorizableRequest, ICachedQuery
{
    public string Action => "department.read";
    public string Resource => $"tenant:{TenantId}:department:{DepartmentId}";
    public string CacheKey => $"department:{TenantId:N}:{DepartmentId:N}";
    public TimeSpan? AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(5);
}

public sealed record DepartmentDto(
    Guid Id,
    string Name,
    string? Description,
    string Status);