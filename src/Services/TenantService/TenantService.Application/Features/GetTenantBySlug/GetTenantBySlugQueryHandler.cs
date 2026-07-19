using MediatR;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Results;
using TenantService.Domain.Enums;
using TenantService.Domain.Errors;
using TenantService.Domain.Repositories;

namespace TenantService.Application.Features.GetTenantBySlug;

public sealed class GetTenantBySlugQueryHandler(ITenantRepository tenantRepository)
    : IRequestHandler<GetTenantBySlugQuery, Result<TenantSlugDto>>
{
    public async Task<Result<TenantSlugDto>> Handle(GetTenantBySlugQuery request, CancellationToken cancellationToken)
    {
        var slugResult = TenantSlug.Create(request.Slug);
        if (slugResult.IsFailure)
            return Result<TenantSlugDto>.Failure(TenantErrors.TenantNotFound);

        var tenant = await tenantRepository.GetBySlugAsync(slugResult.Value!, cancellationToken: cancellationToken);
        if (tenant is null || tenant.Status is TenantStatus.Suspended or TenantStatus.Disabled)
            return Result<TenantSlugDto>.Failure(TenantErrors.TenantNotFound);

        return Result<TenantSlugDto>.Success(new TenantSlugDto(
            tenant.Id, tenant.Name, tenant.Slug.Value));
    }
}
