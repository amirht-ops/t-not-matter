using FluentValidation;

namespace IdentityService.Application.Features.UnlockUser;

public sealed class UnlockUserCommandValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
    }
}