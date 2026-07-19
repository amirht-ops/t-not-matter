using FluentValidation;
using IdentityService.Api.Contracts.Requests;

namespace IdentityService.Api.Validation;

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(request => request.SessionId).NotEmpty();
    }
}

public sealed class MfaVerifyRequestValidator : AbstractValidator<MfaVerifyRequest>
{
    public MfaVerifyRequestValidator()
    {
        RuleFor(request => request.UserId).NotEmpty();
        RuleFor(request => request.Code).NotEmpty();
    }
}

public sealed class EnableMfaRequestValidator : AbstractValidator<EnableMfaRequest>
{
    public EnableMfaRequestValidator()
    {
        RuleFor(request => request.UserId).NotEmpty();
        RuleFor(request => request.Secret).NotEmpty();
    }
}
