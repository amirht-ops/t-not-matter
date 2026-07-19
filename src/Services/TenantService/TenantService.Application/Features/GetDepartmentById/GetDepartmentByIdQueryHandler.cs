using MediatR;
using SharedKernel.Results;
using TenantService.Domain.Repositories;

namespace TenantService.Application.Features.GetDepartmentById;

public sealed class GetDepartmentByIdQueryHandler(
    IDepartmentRepository departmentRepository)
    : IRequestHandler<GetDepartmentByIdQuery, Result<DepartmentDto>>
{
    public async Task<Result<DepartmentDto>> Handle(GetDepartmentByIdQuery request, CancellationToken cancellationToken)
    {
        var department = await departmentRepository.GetByIdAsync(
            request.TenantId, request.DepartmentId, cancellationToken: cancellationToken);

        if (department is null)
            return Result<DepartmentDto>.Failure(TenantService.Domain.Errors.TenantErrors.DepartmentNotFound);

        var dto = new DepartmentDto(
            department.Id,
            department.Name.Value,
            department.Description,
            department.Status.ToString());

        return Result<DepartmentDto>.Success(dto);
    }
}