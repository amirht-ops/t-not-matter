using FluentValidation;
using TenantService.Domain.Enums;

namespace TenantService.Application.Features.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandValidator : AbstractValidator<ChangeTenantStatusCommand>
{
    public ChangeTenantStatusCommandValidator()
    {
        RuleFor(request => request.TenantId).NotEmpty()
            .WithMessage("Tenant Id is required.");

        RuleFor(request => request.Status)
            .IsInEnum()
            .WithMessage("Invalid tenant status.")
            .Must(status => status != TenantStatus.Pending)
            .WithMessage("Cannot transition to Pending status.");
    }
}