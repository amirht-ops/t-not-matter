using FluentValidation;
using IdentityService.Api.Contracts.Requests;

namespace IdentityService.Api.Validation;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(request => request.UserName).NotEmpty();
        RuleFor(request => request.PhoneNumber).NotEmpty().MaximumLength(11);
        RuleFor(request => request.Password).NotEmpty().MinimumLength(12);
        When(request => !string.IsNullOrWhiteSpace(request.Email), () =>
        {
            RuleFor(request => request.Email)
                .Matches(@"^[^@\s]+@[^@\s]+\.[^@\s]+$").WithMessage("Email format is invalid.");
        });
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty();

        When(x => !string.IsNullOrWhiteSpace(x.Identifier), () =>
        {
            RuleFor(x => x.Identifier)
                .NotEmpty()
                .Must(id => id!.Contains('.') && id.IndexOf('.') > 0 && id.IndexOf('.') < id.Length - 1)
                .WithMessage("Identifier must be in the format 'slug.username'.");

            RuleFor(x => x.UserName).Null()
                .WithMessage("UserName must not be provided when using identifier.");
        });

        When(x => string.IsNullOrWhiteSpace(x.Identifier), () =>
        {
            RuleFor(x => x.UserName).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(request => request.RefreshToken).NotEmpty();
    }
}
