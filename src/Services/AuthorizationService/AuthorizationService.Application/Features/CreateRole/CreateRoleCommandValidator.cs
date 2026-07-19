using FluentValidation;

namespace AuthorizationService.Application.Features.CreateRole;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Description).MaximumLength(512);
        // A role must belong to a real department. An empty guid previously slipped past
        // validation and surfaced downstream as a 500 (General.ServiceUnavailable); reject it here.
        RuleFor(command => command.DepartmentId).NotEmpty();
    }
}
