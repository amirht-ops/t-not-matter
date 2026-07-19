using MediatR;
using SharedKernel.Results;
using TenantService.Application.Features.GetDepartmentById;
using TenantService.Domain.Repositories;

namespace TenantService.Application.Features.ListDepartments;

public sealed class ListDepartmentsQueryHandler(
    IDepartmentRepository departmentRepository) : IRequestHandler<ListDepartmentsQuery, Result<IReadOnlyList<DepartmentDto>>>
{
    public async Task<Result<IReadOnlyList<DepartmentDto>>> Handle(ListDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var departments = await departmentRepository.GetByTenantIdAsync(request.TenantId, cancellationToken: cancellationToken);

        var dtos = departments
            .Select(d => new DepartmentDto(
                d.Id,
                d.Name.Value,
                d.Description,
                d.Status.ToString()))
            .ToList();

        return Result<IReadOnlyList<DepartmentDto>>.Success(dtos);
    }
}
