using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Subscriptions;

public sealed record SubscriptionDto(Guid Id, Guid PolicyId, string Scope, string Status, DateTimeOffset EffectiveFrom);

public sealed record GetSubscriptionByIdQuery(Guid SubscriptionId) : IRequest<Result<SubscriptionDto>>
{
}

public sealed class GetSubscriptionByIdQueryValidator : AbstractValidator<GetSubscriptionByIdQuery>
{
    public GetSubscriptionByIdQueryValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
    }
}

public sealed class GetSubscriptionByIdQueryHandler(ISubscriptionRepository subscriptions, IRequestContextAccessor requestContext)
    : IRequestHandler<GetSubscriptionByIdQuery, Result<SubscriptionDto>>
{
    public static SubscriptionDto Map(Subscription subscription) => new(
        subscription.Id,
        subscription.PolicyId.Value,
        subscription.Scope.ToString(),
        subscription.Status.ToString(),
        subscription.EffectiveFrom);

    public async Task<Result<SubscriptionDto>> Handle(GetSubscriptionByIdQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var subscription = await subscriptions.GetByIdAsync(context.TenantId, SubscriptionId.From(request.SubscriptionId), cancellationToken: cancellationToken);
        if (subscription is null)
            return Result<SubscriptionDto>.Failure(GeneralErrors.NotFound);

        return Result<SubscriptionDto>.Success(Map(subscription));
    }
}
