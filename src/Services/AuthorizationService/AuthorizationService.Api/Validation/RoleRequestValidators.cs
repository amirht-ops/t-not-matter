using AuthorizationService.Api.Contracts.Requests;
using FluentValidation;

namespace AuthorizationService.Api.Validation;

public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(128);
        RuleFor(request => request.Description).MaximumLength(512);
        RuleFor(request => request.DepartmentId).NotEmpty();
    }
}

public sealed class AssignRoleRequestValidator : AbstractValidator<AssignRoleRequest>
{
    public AssignRoleRequestValidator()
    {
        RuleFor(request => request.SubjectId).NotEmpty();
        RuleFor(request => request.RoleId).NotEmpty();
        RuleFor(request => request.AssignedBy).NotEmpty();
    }
}

public sealed class RevokeRoleRequestValidator : AbstractValidator<RevokeRoleRequest>
{
    public RevokeRoleRequestValidator()
    {
        RuleFor(request => request.SubjectId).NotEmpty();
        RuleFor(request => request.RoleId).NotEmpty();
    }
}

public sealed class ChangeUserRoleRequestValidator : AbstractValidator<ChangeUserRoleRequest>
{
    public ChangeUserRoleRequestValidator()
    {
        RuleFor(request => request.SubjectId).NotEmpty();
        RuleFor(request => request.NewRoleId).NotEmpty();
        RuleFor(request => request.AssignedBy).NotEmpty();
    }
}
