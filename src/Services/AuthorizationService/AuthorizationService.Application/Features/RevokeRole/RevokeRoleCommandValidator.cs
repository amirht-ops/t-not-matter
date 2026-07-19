using FluentValidation;

namespace AuthorizationService.Application.Features.RevokeRole;

public sealed class RevokeRoleCommandValidator : AbstractValidator<RevokeRoleCommand>
{
    public RevokeRoleCommandValidator()
    {
        RuleFor(command => command.SubjectId).NotEmpty();
        RuleFor(command => command.RoleId).NotEmpty();
    }
}
