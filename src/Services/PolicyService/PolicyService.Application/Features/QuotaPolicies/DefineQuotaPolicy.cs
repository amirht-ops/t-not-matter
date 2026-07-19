using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.QuotaPolicies;

public sealed record DefineQuotaPolicyCommand(PrincipalKind ScopeKind, Guid ScopePrincipalId, long Daily, long Weekly, long Monthly)
    : IRequest<Result<DefineQuotaPolicyResponse>>, ITransactionalRequest, IAuthorizableRequest, IIdempotentRequest
{
    public string IdempotencyKey => $"define-quota:{ScopeKind}:{ScopePrincipalId:N}";
    public string Action => "quota.define";
    public string Resource => $"quota:{ScopeKind}:{ScopePrincipalId:N}";
}

public sealed record DefineQuotaPolicyResponse(Guid QuotaPolicyId);

public sealed class DefineQuotaPolicyCommandValidator : AbstractValidator<DefineQuotaPolicyCommand>
{
    public DefineQuotaPolicyCommandValidator()
    {
        RuleFor(x => x.ScopeKind).IsInEnum();
        RuleFor(x => x.ScopePrincipalId).NotEmpty();
        RuleFor(x => x.Daily).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Weekly).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Monthly).GreaterThanOrEqualTo(0);
    }
}

public sealed class DefineQuotaPolicyCommandHandler(IQuotaPolicyRepository quotaPolicies, IRequestContextAccessor requestContext)
    : IRequestHandler<DefineQuotaPolicyCommand, Result<DefineQuotaPolicyResponse>>
{
    public async Task<Result<DefineQuotaPolicyResponse>> Handle(DefineQuotaPolicyCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var scope = SubscriptionScope.Create(command.ScopeKind, command.ScopePrincipalId);
        if (scope.IsFailure)
            return Result<DefineQuotaPolicyResponse>.Failure(scope.Error);

        var daily = QuotaLimit.Create(command.Daily);
        var weekly = QuotaLimit.Create(command.Weekly);
        var monthly = QuotaLimit.Create(command.Monthly);
        if (daily.IsFailure || weekly.IsFailure || monthly.IsFailure)
            return Result<DefineQuotaPolicyResponse>.Failure(daily.IsFailure ? daily.Error : weekly.IsFailure ? weekly.Error : monthly.Error);

        var quota = Quota.Create(daily.Value!, weekly.Value!, monthly.Value!);
        if (quota.IsFailure)
            return Result<DefineQuotaPolicyResponse>.Failure(quota.Error);

        var result = QuotaPolicy.Create(context.TenantId, scope.Value!, quota.Value!, context.CorrelationId);
        if (result.IsFailure)
            return Result<DefineQuotaPolicyResponse>.Failure(result.Error);

        var quotaPolicy = result.Value!;
        await quotaPolicies.AddAsync(quotaPolicy, cancellationToken);

        return Result<DefineQuotaPolicyResponse>.Success(new DefineQuotaPolicyResponse(quotaPolicy.Id));
    }
}
