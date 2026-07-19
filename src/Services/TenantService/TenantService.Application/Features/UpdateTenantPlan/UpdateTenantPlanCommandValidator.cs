using FluentValidation;

namespace TenantService.Application.Features.UpdateTenantPlan;

public sealed class UpdateTenantPlanCommandValidator : AbstractValidator<UpdateTenantPlanCommand>
{
    public UpdateTenantPlanCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty()
            .WithMessage("Tenant ID is required.");

        RuleFor(x => x.NewPlanTier)
            .IsInEnum()
            .WithMessage("Invalid plan tier.");
    }
}