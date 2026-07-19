using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Subscriptions;

public sealed record ListSubscriptionsByPolicyQuery(Guid PolicyId)
    : IRequest<Result<IReadOnlyCollection<SubscriptionDto>>>
{
}

public sealed class ListSubscriptionsByPolicyQueryValidator : AbstractValidator<ListSubscriptionsByPolicyQuery>
{
    public ListSubscriptionsByPolicyQueryValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}

public sealed class ListSubscriptionsByPolicyQueryHandler(ISubscriptionRepository subscriptions, IRequestContextAccessor requestContext)
    : IRequestHandler<ListSubscriptionsByPolicyQuery, Result<IReadOnlyCollection<SubscriptionDto>>>
{
    public async Task<Result<IReadOnlyCollection<SubscriptionDto>>> Handle(ListSubscriptionsByPolicyQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var result = await subscriptions.ListByPolicyAsync(context.TenantId, PolicyId.From(request.PolicyId), cancellationToken);
        var dtos = result.Select(GetSubscriptionByIdQueryHandler.Map).ToList();
        return Result<IReadOnlyCollection<SubscriptionDto>>.Success(dtos.AsReadOnly());
    }
}
