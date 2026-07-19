using FluentValidation;

namespace AuthorizationService.Application.Features.CreatePermission;

public sealed class CreatePermissionCommandValidator : AbstractValidator<CreatePermissionCommand>
{
    public CreatePermissionCommandValidator()
    {
        RuleFor(command => command.Key).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Action).NotEmpty().MaximumLength(128);
        RuleFor(command => command.ResourceType).NotEmpty().MaximumLength(128);
    }
}
