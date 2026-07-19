using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.QuotaPolicies;

public sealed record AmendQuotaPolicyCommand(Guid QuotaPolicyId, long Daily, long Weekly, long Monthly)
    : IRequest<Result<Unit>>, ITransactionalRequest, IAuthorizableRequest
{
    public string Action => "quota.amend";
    public string Resource => $"quota:{QuotaPolicyId:N}";
}

public sealed class AmendQuotaPolicyCommandValidator : AbstractValidator<AmendQuotaPolicyCommand>
{
    public AmendQuotaPolicyCommandValidator()
    {
        RuleFor(x => x.QuotaPolicyId).NotEmpty();
        RuleFor(x => x.Daily).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Weekly).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Monthly).GreaterThanOrEqualTo(0);
    }
}

public sealed class AmendQuotaPolicyCommandHandler(IQuotaPolicyRepository quotaPolicies, IRequestContextAccessor requestContext)
    : IRequestHandler<AmendQuotaPolicyCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(AmendQuotaPolicyCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var daily = QuotaLimit.Create(command.Daily);
        var weekly = QuotaLimit.Create(command.Weekly);
        var monthly = QuotaLimit.Create(command.Monthly);
        if (daily.IsFailure || weekly.IsFailure || monthly.IsFailure)
            return Result<Unit>.Failure(daily.IsFailure ? daily.Error : weekly.IsFailure ? weekly.Error : monthly.Error);

        var quota = Quota.Create(daily.Value!, weekly.Value!, monthly.Value!);
        if (quota.IsFailure)
            return Result<Unit>.Failure(quota.Error);

        var quotaPolicy = await quotaPolicies.GetByIdAsync(context.TenantId, QuotaPolicyId.From(command.QuotaPolicyId), trackChanges: true, cancellationToken: cancellationToken);
        if (quotaPolicy is null)
            return Result<Unit>.Failure(GeneralErrors.NotFound);

        var result = quotaPolicy.Amend(quota.Value!, context.CorrelationId);
        if (result.IsFailure)
            return Result<Unit>.Failure(result.Error);

        await quotaPolicies.UpdateAsync(quotaPolicy, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
