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

public sealed record RemoveQuotaPolicyCommand(Guid QuotaPolicyId)
    : IRequest<Result<Unit>>, ITransactionalRequest, IAuthorizableRequest
{
    public string Action => "quota.remove";
    public string Resource => $"quota:{QuotaPolicyId:N}";
}

public sealed class RemoveQuotaPolicyCommandValidator : AbstractValidator<RemoveQuotaPolicyCommand>
{
    public RemoveQuotaPolicyCommandValidator()
    {
        RuleFor(x => x.QuotaPolicyId).NotEmpty();
    }
}

public sealed class RemoveQuotaPolicyCommandHandler(IQuotaPolicyRepository quotaPolicies, IRequestContextAccessor requestContext)
    : IRequestHandler<RemoveQuotaPolicyCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(RemoveQuotaPolicyCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var quotaPolicy = await quotaPolicies.GetByIdAsync(context.TenantId, QuotaPolicyId.From(command.QuotaPolicyId), trackChanges: true, cancellationToken: cancellationToken);
        if (quotaPolicy is null)
            return Result<Unit>.Failure(GeneralErrors.NotFound);

        var result = quotaPolicy.Remove(context.CorrelationId);
        if (result.IsFailure)
            return Result<Unit>.Failure(result.Error);

        await quotaPolicies.UpdateAsync(quotaPolicy, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
