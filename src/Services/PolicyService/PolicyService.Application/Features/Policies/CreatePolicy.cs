using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Errors;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Policies;

public sealed record CreatePolicyCommand(string Name, string Condition, string Expression, int Priority = 100)
    : IRequest<Result<CreatePolicyResponse>>, ITransactionalRequest, IAuthorizableRequest, IIdempotentRequest
{
    public string IdempotencyKey => $"create-policy:{Name}";
    public string Action => "policy.create";
    public string Resource => "policy";
}

public sealed record CreatePolicyResponse(Guid PolicyId);

public sealed class CreatePolicyCommandValidator : AbstractValidator<CreatePolicyCommand>
{
    public CreatePolicyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Condition).NotEmpty();
        RuleFor(x => x.Expression).NotEmpty();
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreatePolicyCommandHandler(IPolicyRepository policies, IRequestContextAccessor requestContext)
    : IRequestHandler<CreatePolicyCommand, Result<CreatePolicyResponse>>
{
    public async Task<Result<CreatePolicyResponse>> Handle(CreatePolicyCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var condition = PolicyCondition.Create(command.Condition);
        if (condition.IsFailure)
            return Result<CreatePolicyResponse>.Failure(condition.Error);

        var expression = PolicyExpression.Create(command.Expression);
        if (expression.IsFailure)
            return Result<CreatePolicyResponse>.Failure(expression.Error);

        var priority = PolicyPriority.Create(command.Priority);
        if (priority.IsFailure)
            return Result<CreatePolicyResponse>.Failure(priority.Error);

        var result = Policy.Create(context.TenantId, command.Name, condition.Value!, expression.Value!, priority.Value!, context.CorrelationId);
        if (result.IsFailure)
            return Result<CreatePolicyResponse>.Failure(result.Error);

        var policy = result.Value!;
        await policies.AddAsync(policy, cancellationToken);

        return Result<CreatePolicyResponse>.Success(new CreatePolicyResponse(policy.Id));
    }
}
