using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SKResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;
using TenantService.Domain.Enums;

namespace TenantService.Application.Features.UpdateTenantPlan;

public sealed record UpdateTenantPlanCommand(
    Guid TenantId,
    PlanTier NewPlanTier) : IRequest<SKResult>, IAuthorizableRequest, ITransactionalRequest, IIdempotentRequest
{
    public string Action => "tenant.plans.update";
    public string Resource => $"tenant:{TenantId}";
    public string IdempotencyKey => $"update-tenant-plan:{TenantId:N}:{NewPlanTier}";
}