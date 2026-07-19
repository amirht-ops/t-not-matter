using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;
using TenantService.Domain.Enums;

namespace TenantService.Application.Features.ChangeTenantStatus;

public sealed record ChangeTenantStatusCommand(
    Guid TenantId,
    TenantStatus Status,
    Guid CorrelationId) : IRequest<SKResult>, IAuthorizableRequest, ITransactionalRequest, IIdempotentRequest
{
    public string Action => Status switch
    {
        TenantStatus.Active => "tenant.activate",
        TenantStatus.Disabled => "tenant.disable",
        TenantStatus.Suspended => "tenant.suspend",
        _ => "tenant.status.change"
    };

    public string Resource => $"tenant:{TenantId}";
    public string IdempotencyKey => $"change-tenant-status:{CorrelationId:N}";
}