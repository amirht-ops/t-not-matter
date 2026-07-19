using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.QuotaPolicies;

public sealed record QuotaPolicyDto(Guid Id, string Scope, long Daily, long Weekly, long Monthly, bool IsEnabled);

public sealed record GetQuotaPolicyByIdQuery(Guid QuotaPolicyId) : IRequest<Result<QuotaPolicyDto>>
{
}

public sealed class GetQuotaPolicyByIdQueryValidator : AbstractValidator<GetQuotaPolicyByIdQuery>
{
    public GetQuotaPolicyByIdQueryValidator()
    {
        RuleFor(x => x.QuotaPolicyId).NotEmpty();
    }
}

public sealed class GetQuotaPolicyByIdQueryHandler(IQuotaPolicyRepository quotaPolicies, IRequestContextAccessor requestContext)
    : IRequestHandler<GetQuotaPolicyByIdQuery, Result<QuotaPolicyDto>>
{
    public static QuotaPolicyDto Map(QuotaPolicy quotaPolicy) => new(
        quotaPolicy.Id,
        quotaPolicy.Scope.ToString(),
        quotaPolicy.Quota.Daily.Value,
        quotaPolicy.Quota.Weekly.Value,
        quotaPolicy.Quota.Monthly.Value,
        quotaPolicy.IsEnabled);

    public async Task<Result<QuotaPolicyDto>> Handle(GetQuotaPolicyByIdQuery request, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var quotaPolicy = await quotaPolicies.GetByIdAsync(context.TenantId, QuotaPolicyId.From(request.QuotaPolicyId), cancellationToken: cancellationToken);
        if (quotaPolicy is null)
            return Result<QuotaPolicyDto>.Failure(GeneralErrors.NotFound);

        return Result<QuotaPolicyDto>.Success(Map(quotaPolicy));
    }
}
