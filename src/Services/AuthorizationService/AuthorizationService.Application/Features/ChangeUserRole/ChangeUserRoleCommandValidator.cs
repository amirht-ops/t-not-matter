using FluentValidation;

namespace AuthorizationService.Application.Features.ChangeUserRole;

public sealed class ChangeUserRoleCommandValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleCommandValidator()
    {
        RuleFor(command => command.SubjectId).NotEmpty();
        RuleFor(command => command.NewRoleId).NotEmpty();
        RuleFor(command => command.AssignedBy).NotEmpty();
    }
}
