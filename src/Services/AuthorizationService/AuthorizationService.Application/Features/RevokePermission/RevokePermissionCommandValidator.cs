using FluentValidation;

namespace AuthorizationService.Application.Features.RevokePermission;

public sealed class RevokePermissionCommandValidator : AbstractValidator<RevokePermissionCommand>
{
    public RevokePermissionCommandValidator()
    {
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.PermissionId).NotEmpty();
    }
}
