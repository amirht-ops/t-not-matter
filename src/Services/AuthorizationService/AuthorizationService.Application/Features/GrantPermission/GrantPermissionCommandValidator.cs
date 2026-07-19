using FluentValidation;

namespace AuthorizationService.Application.Features.GrantPermission;

public sealed class GrantPermissionCommandValidator : AbstractValidator<GrantPermissionCommand>
{
    public GrantPermissionCommandValidator()
    {
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.PermissionId).NotEmpty();
        RuleFor(command => command.GrantedBy).NotEmpty();
    }
}
