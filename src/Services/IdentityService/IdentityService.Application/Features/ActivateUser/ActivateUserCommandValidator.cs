using FluentValidation;

namespace IdentityService.Application.Features.ActivateUser;

public sealed class ActivateUserCommandValidator : AbstractValidator<ActivateUserCommand>
{
    public ActivateUserCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
    }
}
