using AuthorizationService.Api.Contracts.Requests;
using FluentValidation;

namespace AuthorizationService.Api.Validation;

public sealed class EvaluateAuthorizationRequestValidator : AbstractValidator<EvaluateAuthorizationRequest>
{
    public EvaluateAuthorizationRequestValidator()
    {
        RuleFor(request => request.SubjectId).NotEmpty();
        RuleFor(request => request.Action).NotEmpty().MaximumLength(128);
        RuleFor(request => request.ResourceType).NotEmpty().MaximumLength(128);
        RuleFor(request => request.ResourceId).NotEmpty().MaximumLength(256);
    }
}

public sealed class BatchEvaluateAuthorizationRequestValidator : AbstractValidator<BatchEvaluateAuthorizationRequest>
{
    public BatchEvaluateAuthorizationRequestValidator() => RuleFor(request => request.Decisions).NotEmpty().Must(items => items.Count <= 100);
}
