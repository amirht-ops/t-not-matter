using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Policies;

public sealed record ChangePolicyConditionCommand(Guid PolicyId, string Condition, string Expression)
    : IRequest<Result<Unit>>, ITransactionalRequest, IAuthorizableRequest
{
    public string Action => "policy.change-condition";
    public string Resource => $"policy:{PolicyId:N}";
}

public sealed class ChangePolicyConditionCommandValidator : AbstractValidator<ChangePolicyConditionCommand>
{
    public ChangePolicyConditionCommandValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
        RuleFor(x => x.Condition).NotEmpty();
        RuleFor(x => x.Expression).NotEmpty();
    }
}

public sealed class ChangePolicyConditionCommandHandler(IPolicyRepository policies, IRequestContextAccessor requestContext)
    : IRequestHandler<ChangePolicyConditionCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(ChangePolicyConditionCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var condition = PolicyCondition.Create(command.Condition);
        if (condition.IsFailure)
            return Result<Unit>.Failure(condition.Error);

        var expression = PolicyExpression.Create(command.Expression);
        if (expression.IsFailure)
            return Result<Unit>.Failure(expression.Error);

        var policy = await policies.GetByIdAsync(context.TenantId, PolicyId.From(command.PolicyId), trackChanges: true, cancellationToken: cancellationToken);
        if (policy is null)
            return Result<Unit>.Failure(GeneralErrors.NotFound);

        var change = policy.ChangeCondition(condition.Value!, expression.Value!, context.CorrelationId);
        if (change.IsFailure)
            return Result<Unit>.Failure(change.Error);

        await policies.UpdateAsync(policy, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
