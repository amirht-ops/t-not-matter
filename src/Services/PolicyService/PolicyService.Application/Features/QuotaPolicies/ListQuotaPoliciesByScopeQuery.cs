using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Application.Features.QuotaPolicies;

public sealed record ListQuotaPoliciesByScopeQuery(PrincipalKind ScopeKind, Guid ScopePrincipalId)
    : IRequest<Result<IReadOnlyCollection<QuotaPolicyDto>>>
{
}

public sealed class ListQuotaPoliciesByScopeQueryValidator : AbstractValidator<ListQuotaPoliciesByScopeQuery>
{
    public ListQuotaPoliciesByScopeQueryValidator()
    {
        RuleFor(x => x.ScopePrincipalId).NotEmpty();
    }
}

public sealed class ListQuotaPoliciesByScopeQueryHandler(IQuotaPolicyRepository quotaPolicies, IRequestContextAccessor requestContext)
    : IRequestHandler<ListQuotaPoliciesByScopeQuery, Result<IReadOnlyCollection<QuotaPolicyDto>>>
{
    public async Task<Result<IReadOnlyCollection<QuotaPolicyDto>>> Handle(ListQuotaPoliciesByScopeQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var scope = SubscriptionScope.Create(request.ScopeKind, request.ScopePrincipalId);
        if (scope.IsFailure)
            return Result<IReadOnlyCollection<QuotaPolicyDto>>.Failure(scope.Error);

        var result = await quotaPolicies.ListByScopeAsync(context.TenantId, scope.Value!, cancellationToken);
        var dtos = result.Select(GetQuotaPolicyByIdQueryHandler.Map).ToList();
        return Result<IReadOnlyCollection<QuotaPolicyDto>>.Success(dtos.AsReadOnly());
    }
}
