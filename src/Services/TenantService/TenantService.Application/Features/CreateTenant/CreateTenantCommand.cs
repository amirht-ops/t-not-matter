using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;
using TenantService.Domain.Enums;

namespace TenantService.Application.Features.CreateTenant;

public sealed record CreateTenantCommand(
    string Name,
    string Identifier,
    string Slug,
    PlanTier PlanTier,
    Guid CorrelationId) : IRequest<Result<Guid>>, IAuthorizableRequest, ITransactionalRequest, IIdempotentRequest
{
    public string Action => "tenant.create";
    public string Resource => "tenant:";
    public string IdempotencyKey => $"create-tenant:{CorrelationId:N}";
}