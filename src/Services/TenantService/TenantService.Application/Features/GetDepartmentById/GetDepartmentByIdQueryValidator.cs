using FluentValidation;

namespace TenantService.Application.Features.GetDepartmentById;

public class GetDepartmentByIdQueryValidator : AbstractValidator<GetDepartmentByIdQuery>
{
    public GetDepartmentByIdQueryValidator()
    {
        RuleFor(request => request.TenantId).NotEmpty();
        RuleFor(request => request.DepartmentId).NotEmpty();
    }
}