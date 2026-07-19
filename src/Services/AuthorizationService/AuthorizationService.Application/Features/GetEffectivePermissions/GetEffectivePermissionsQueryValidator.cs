using FluentValidation;

namespace AuthorizationService.Application.Features.GetEffectivePermissions;

public sealed class GetEffectivePermissionsQueryValidator : AbstractValidator<GetEffectivePermissionsQuery>
{
    public GetEffectivePermissionsQueryValidator()
    {
        RuleFor(query => query.SubjectId).NotEmpty();
    }
}
