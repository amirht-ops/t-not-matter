using MediatR;
using SharedKernel.Results;
using TenantService.Domain.Enums;

namespace TenantService.Application.Features.GetCurrentTenant;

public sealed record GetCurrentTenantQuery(Guid TenantId, Guid CorrelationId) : IRequest<Result<CurrentTenantDto>>;

public sealed record CurrentTenantDto(
    Guid Id,
    string Name,
    string Slug,
    PlanTier PlanTier,
    TenantStatus Status);