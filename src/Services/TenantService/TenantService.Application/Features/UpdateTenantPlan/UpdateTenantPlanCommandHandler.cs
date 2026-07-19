using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;
using TenantService.Domain.Repositories;
using Unit = SharedKernel.Results.Unit;

namespace TenantService.Application.Features.UpdateTenantPlan;

public sealed class UpdateTenantPlanCommandHandler(
    ITenantRepository tenantRepository,
    IRequestContextAccessor requestContextAccessor) : IRequestHandler<UpdateTenantPlanCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(UpdateTenantPlanCommand request, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetByIdAsync(request.TenantId, trackChanges: true, cancellationToken);
        if (tenant is null)
        {
            return Result<Unit>.Failure(TenantService.Domain.Errors.TenantErrors.TenantNotFound);
        }

        var correlationId = requestContextAccessor.Context.CorrelationId;
        var result = tenant.UpgradePlan(request.NewPlanTier, correlationId);
        if (result.IsFailure)
        {
            return result;
        }

        await tenantRepository.UpdateAsync(tenant, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}