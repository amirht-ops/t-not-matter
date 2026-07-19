using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;
using TenantService.Domain.Enums;
using TenantService.Domain.Repositories;
using Unit = SharedKernel.Results.Unit;

namespace TenantService.Application.Features.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandHandler(
    ITenantRepository tenantRepository,
    IRequestContextAccessor requestContextAccessor) : IRequestHandler<ChangeTenantStatusCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(ChangeTenantStatusCommand request, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetByIdAsync(request.TenantId, trackChanges: true, cancellationToken);
        if (tenant is null)
        {
            return Result<Unit>.Failure(Domain.Errors.TenantErrors.TenantNotFound);
        }

        var correlationId = requestContextAccessor.Context.CorrelationId;

        Result<Unit> result = request.Status switch
        {
            TenantStatus.Active => tenant.Activate(correlationId),
            TenantStatus.Disabled => tenant.Deactivate(correlationId),
            TenantStatus.Suspended => tenant.SuspendForNonPayment(correlationId),
            _ => Result<Unit>.Failure(TenantService.Domain.Errors.TenantErrors.InvalidStatusTransition)
        };

        if (result.IsFailure)
        {
            return result;
        }

        await tenantRepository.UpdateAsync(tenant, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}