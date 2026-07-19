using MediatR;
using SharedKernel.Results;
using TenantService.Domain.Errors;
using TenantService.Domain.Repositories;

namespace TenantService.Application.Features.GetCurrentTenant;

public sealed class GetCurrentTenantQueryHandler(ITenantRepository tenantRepository)
    : IRequestHandler<GetCurrentTenantQuery, Result<CurrentTenantDto>>
{
    public async Task<Result<CurrentTenantDto>> Handle(GetCurrentTenantQuery request, CancellationToken ct)
    {
        var tenant = await tenantRepository.GetByIdAsync(request.TenantId, cancellationToken: ct);
        if (tenant is null)
            return Result<CurrentTenantDto>.Failure(TenantErrors.TenantNotFound);

        return Result<CurrentTenantDto>.Success(new CurrentTenantDto(
            tenant.Id, tenant.Name, tenant.Slug.Value, tenant.PlanTier, tenant.Status));
    }
}