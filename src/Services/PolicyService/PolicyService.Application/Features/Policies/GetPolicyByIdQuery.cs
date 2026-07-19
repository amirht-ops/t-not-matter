using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Policies;

public sealed record PolicyDto(Guid Id, string Name, string Condition, string Expression, int Priority, string Status, string? CompiledRegoHash);

public sealed record GetPolicyByIdQuery(Guid PolicyId) : IRequest<Result<PolicyDto>>
{
}

public sealed class GetPolicyByIdQueryValidator : AbstractValidator<GetPolicyByIdQuery>
{
    public GetPolicyByIdQueryValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}

public sealed class GetPolicyByIdQueryHandler(IPolicyRepository policies, IRequestContextAccessor requestContext)
    : IRequestHandler<GetPolicyByIdQuery, Result<PolicyDto>>
{
    public static PolicyDto Map(Policy policy) => new(
        policy.Id,
        policy.Name,
        policy.Condition.Value,
        policy.Expression.Value,
        policy.Priority.Rank,
        policy.Status.ToString(),
        policy.CompiledRego?.Hash);

    public async Task<Result<PolicyDto>> Handle(GetPolicyByIdQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var policy = await policies.GetByIdAsync(context.TenantId, PolicyId.From(request.PolicyId), cancellationToken: cancellationToken);
        if (policy is null)
            return Result<PolicyDto>.Failure(GeneralErrors.NotFound);

        return Result<PolicyDto>.Success(Map(policy));
    }
}
