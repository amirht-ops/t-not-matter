using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Repositories;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Policies;

public sealed record ListPoliciesByTenantQuery : IRequest<Result<IReadOnlyCollection<PolicyDto>>>
{
}

public sealed class ListPoliciesByTenantQueryValidator : AbstractValidator<ListPoliciesByTenantQuery>
{
}

public sealed class ListPoliciesByTenantQueryHandler(IPolicyRepository policies, IRequestContextAccessor requestContext)
    : IRequestHandler<ListPoliciesByTenantQuery, Result<IReadOnlyCollection<PolicyDto>>>
{
    public async Task<Result<IReadOnlyCollection<PolicyDto>>> Handle(ListPoliciesByTenantQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var policiesResult = await policies.ListByTenantAsync(context.TenantId, cancellationToken);
        var dtos = policiesResult.Select(GetPolicyByIdQueryHandler.Map).ToList();
        return Result<IReadOnlyCollection<PolicyDto>>.Success(dtos.AsReadOnly());
    }
}
