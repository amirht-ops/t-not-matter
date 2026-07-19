using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Subscriptions;

public sealed record RevokeSubscriptionCommand(Guid SubscriptionId)
    : IRequest<Result<Unit>>, ITransactionalRequest, IAuthorizableRequest
{
    public string Action => "subscription.revoke";
    public string Resource => $"subscription:{SubscriptionId:N}";
}

public sealed class RevokeSubscriptionCommandValidator : AbstractValidator<RevokeSubscriptionCommand>
{
    public RevokeSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
    }
}

public sealed class RevokeSubscriptionCommandHandler(ISubscriptionRepository subscriptions, IRequestContextAccessor requestContext)
    : IRequestHandler<RevokeSubscriptionCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(RevokeSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var subscription = await subscriptions.GetByIdAsync(context.TenantId, SubscriptionId.From(command.SubscriptionId), trackChanges: true, cancellationToken: cancellationToken);
        if (subscription is null)
            return Result<Unit>.Failure(GeneralErrors.NotFound);

        var result = subscription.Revoke(context.CorrelationId);
        if (result.IsFailure)
            return Result<Unit>.Failure(result.Error);

        await subscriptions.UpdateAsync(subscription, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
