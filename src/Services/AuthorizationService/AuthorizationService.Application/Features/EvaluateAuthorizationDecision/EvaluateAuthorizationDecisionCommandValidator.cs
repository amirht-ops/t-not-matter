using FluentValidation;

namespace AuthorizationService.Application.Features.EvaluateAuthorizationDecision;

public sealed class EvaluateAuthorizationDecisionCommandValidator : AbstractValidator<EvaluateAuthorizationDecisionCommand>
{
    public EvaluateAuthorizationDecisionCommandValidator()
    {
        RuleFor(command => command.Action).NotEmpty().MaximumLength(128);
        RuleFor(command => command.ResourceType).NotEmpty().MaximumLength(128);
        RuleFor(command => command.ResourceId).NotEmpty().MaximumLength(256);
    }
}
