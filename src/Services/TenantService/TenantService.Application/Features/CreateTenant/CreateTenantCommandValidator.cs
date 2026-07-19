using FluentValidation;

namespace TenantService.Application.Features.CreateTenant;

public sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tenant name is required.")
            .MaximumLength(100)
            .WithMessage("Tenant name cannot exceed 100 characters.");

        RuleFor(x => x.Identifier)
            .NotEmpty()
            .WithMessage("Tenant identifier is required.")
            .Matches(@"^tnnt_[a-z0-9]{22}$")
            .WithMessage("Tenant identifier must match format 'tnnt_[a-z0-9]{22}'.");

        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithMessage("Tenant slug is required.")
            .MinimumLength(2)
            .WithMessage("Tenant slug must be at least 2 characters.")
            .MaximumLength(63)
            .WithMessage("Tenant slug cannot exceed 63 characters.")
            .Matches(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")
            .WithMessage("Tenant slug must be lowercase alphanumeric with hyphens, starting and ending with a letter or digit.");

        RuleFor(x => x.PlanTier)
            .IsInEnum()
            .WithMessage("Invalid plan tier.");
    }
}
