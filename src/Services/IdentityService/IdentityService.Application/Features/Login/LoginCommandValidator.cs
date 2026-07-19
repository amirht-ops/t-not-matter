using FluentValidation;

namespace IdentityService.Application.Features.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    private static readonly char[] SeparatorChars = ['.'];

    public LoginCommandValidator()
    {
        RuleFor(x => x.Password).NotEmpty();

        When(x => !string.IsNullOrWhiteSpace(x.Identifier), () =>
        {
            RuleFor(x => x.Identifier)
                .NotEmpty()
                .Must(id => id!.Contains('.') && id.IndexOf('.') > 0 && id.IndexOf('.') < id.Length - 1)
                .WithMessage("Identifier must be in the format 'slug.username'.");

            RuleFor(x => x.Username).Null()
                .WithMessage("Username must not be provided when using identifier.");
        });

        When(x => string.IsNullOrWhiteSpace(x.Identifier), () =>
        {
            RuleFor(x => x.Username).NotEmpty().MaximumLength(50);
        });
    }
}
