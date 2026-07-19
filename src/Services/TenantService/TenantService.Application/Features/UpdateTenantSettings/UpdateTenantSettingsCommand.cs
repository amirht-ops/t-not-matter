using MediatR;
using SharedKernel.Application;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace TenantService.Application.Features.UpdateTenantSettings;

public sealed record UpdateTenantSettingsCommand(
    Guid TenantId,
    string Name,
    Guid CorrelationId) : IRequest<Result<Unit>>, ITransactionalRequest;