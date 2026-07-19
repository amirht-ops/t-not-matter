using AuthorizationService.Api.Contracts.Requests;
using FluentValidation;

namespace AuthorizationService.Api.Validation;

public sealed class GrantPermissionRequestValidator : AbstractValidator<GrantPermissionRequest>
{
    public GrantPermissionRequestValidator()
    {
        RuleFor(request => request.RoleId).NotEmpty();
        RuleFor(request => request.PermissionId).NotEmpty();
        RuleFor(request => request.GrantedBy).NotEmpty();
    }
}
