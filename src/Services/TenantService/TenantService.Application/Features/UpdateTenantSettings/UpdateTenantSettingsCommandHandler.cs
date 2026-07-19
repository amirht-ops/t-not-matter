using MediatR;
using SharedKernel.Results;
using TenantService.Domain.Errors;
using TenantService.Domain.Repositories;
using Unit = SharedKernel.Results.Unit;

namespace TenantService.Application.Features.UpdateTenantSettings;

public sealed class UpdateTenantSettingsCommandHandler(
    ITenantRepository tenantRepository)
    : IRequestHandler<UpdateTenantSettingsCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(UpdateTenantSettingsCommand request, CancellationToken ct)
    {
        var tenant = await tenantRepository.GetByIdAsync(request.TenantId, trackChanges: true, ct);
        if (tenant is null)
            return Result<Unit>.Failure(TenantErrors.TenantNotFound);

        var result = tenant.UpdateName(request.Name, request.CorrelationId);
        if (result.IsFailure)
            return Result<Unit>.Failure(result.Error!);

        await tenantRepository.UpdateAsync(tenant, ct);
        return Result<Unit>.Success(Unit.Value);
    }
}