using FluentValidation;

namespace IdentityService.Application.Features.EnableMfa;

public sealed class EnableMfaCommandValidator : AbstractValidator<EnableMfaCommand>
{
    public EnableMfaCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Secret).NotEmpty();
    }
}
