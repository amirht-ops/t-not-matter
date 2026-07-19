using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Subscriptions;

public sealed record AssignSubscriptionCommand(Guid PolicyId, PrincipalKind ScopeKind, Guid ScopePrincipalId)
    : IRequest<Result<AssignSubscriptionResponse>>, ITransactionalRequest, IAuthorizableRequest, IIdempotentRequest
{
    public string IdempotencyKey => $"assign-subscription:{PolicyId:N}:{ScopePrincipalId:N}";
    public string Action => "subscription.assign";
    public string Resource => $"subscription:{ScopeKind}:{ScopePrincipalId:N}";
}

public sealed record AssignSubscriptionResponse(Guid SubscriptionId);

public sealed class AssignSubscriptionCommandValidator : AbstractValidator<AssignSubscriptionCommand>
{
    public AssignSubscriptionCommandValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
        RuleFor(x => x.ScopePrincipalId).NotEmpty();
    }
}

public sealed class AssignSubscriptionCommandHandler(
    IPolicyRepository policies,
    ISubscriptionRepository subscriptions,
    IRequestContextAccessor requestContext)
    : IRequestHandler<AssignSubscriptionCommand, Result<AssignSubscriptionResponse>>
{
    public async Task<Result<AssignSubscriptionResponse>> Handle(AssignSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var policy = await policies.GetByIdAsync(context.TenantId, PolicyId.From(command.PolicyId), cancellationToken: cancellationToken);
        if (policy is null)
            return Result<AssignSubscriptionResponse>.Failure(GeneralErrors.NotFound);

        var scope = SubscriptionScope.Create(command.ScopeKind, command.ScopePrincipalId);
        if (scope.IsFailure)
            return Result<AssignSubscriptionResponse>.Failure(scope.Error);

        var result = Subscription.Create(context.TenantId, PolicyId.From(command.PolicyId), scope.Value!, context.CorrelationId);
        if (result.IsFailure)
            return Result<AssignSubscriptionResponse>.Failure(result.Error);

        var subscription = result.Value!;
        await subscriptions.AddAsync(subscription, cancellationToken);

        return Result<AssignSubscriptionResponse>.Success(new AssignSubscriptionResponse(subscription.Id));
    }
}
